using STU.Api.Families;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using NetTopologySuite.Geometries;
using NetTopologySuite.IO.Converters;
using STU.Application.Security;
using STU.Domain.Auditing;
using STU.Domain.Properties;
using STU.Infrastructure.Identity;
using STU.Infrastructure.Persistence;

namespace STU.Api.Properties;

public static partial class PropertyEndpoints
{
    private static readonly JsonSerializerOptions GeometryJsonOptions=CreateGeometryOptions();

    public static IEndpointRouteBuilder MapPropertyEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var read=endpoints.MapGroup("/api/properties").WithTags("Properties").RequireAuthorization(StuPolicies.PropertiesView).RequireRateLimiting("api");
        read.MapGet("",GetPropertiesAsync);read.MapGet("/map",GetPropertyMapAsync);read.MapGet("/reference-data",GetReferenceDataAsync);read.MapGet("/{id:guid}",GetPropertyAsync);read.MapGet("/{id:guid}/versions",GetVersionsAsync);read.MapGet("/{id:guid}/families",GetOccupancyAsync).RequireAuthorization(StuPolicies.FamiliesView);
        var write=endpoints.MapGroup("/api/properties").WithTags("Properties").RequireAuthorization(StuPolicies.PropertiesManage).RequireRateLimiting("api");
        write.MapGet("/address-suggestion", ReverseAddressAsync);
        Secure(write.MapPost("",CreatePropertyAsync));Secure(write.MapPut("/{id:guid}",UpdatePropertyAsync));Secure(write.MapPost("/{id:guid}/archive",ArchivePropertyAsync));Secure(write.MapPost("/{id:guid}/restore",RestorePropertyAsync));
        var visitRead=endpoints.MapGroup("/api/properties/{propertyId:guid}/visits").WithTags("Visits").RequireAuthorization(StuPolicies.VisitsView,StuPolicies.FamiliesView).RequireRateLimiting("api");visitRead.MapGet("",GetVisitsAsync);
        var settings=endpoints.MapGroup("/api/property-settings").WithTags("Property settings").RequireAuthorization(StuPolicies.TerritoryManage).RequireRateLimiting("api");
        settings.MapGet("/tags",GetTagsAsync);Secure(settings.MapPost("/tags",CreateTagAsync));Secure(settings.MapPut("/tags/{id:guid}",UpdateTagAsync));Secure(settings.MapPost("/tags/{id:guid}/archive",ArchiveTagAsync));Secure(settings.MapPost("/tags/{id:guid}/restore",RestoreTagAsync));
        Secure(settings.MapPut("/coverage/{microregionId:guid}",SaveCoverageRuleAsync));
        return endpoints;
    }

    private static RouteHandlerBuilder Secure(RouteHandlerBuilder route)=>route.WithMetadata(new RequireAntiforgeryTokenAttribute(true));

    private static async Task<IResult> GetPropertiesAsync(Guid? healthUnitId,string? query,Guid? microregionId,string? registrationStatus,string? coverage,string? recordState,HttpContext http,UserManager<ApplicationUser> users,StuDbContext db,bool includeArchived=false,int page=1,int pageSize=100)
    {
        var actor=await ActorAsync(http,users);var scope=Scope(actor,http.User,healthUnitId);if(scope.Error is not null)return scope.Error;page=Math.Max(1,page);pageSize=Math.Clamp(pageSize==0?50:pageSize,1,100);
        var propertyQuery=db.Properties.AsNoTracking().Where(x=>x.HealthUnitId==scope.UnitId);
        if(string.IsNullOrWhiteSpace(recordState)) { if(!includeArchived)propertyQuery=propertyQuery.Where(x=>x.ArchivedAtUtc==null); }
        else if(recordState=="active")propertyQuery=propertyQuery.Where(x=>x.ArchivedAtUtc==null&&x.RegistrationStatus==PropertyRegistrationStatus.Active);
        else if(recordState=="draft")propertyQuery=propertyQuery.Where(x=>x.ArchivedAtUtc==null&&x.RegistrationStatus==PropertyRegistrationStatus.Draft);
        else if(recordState=="archived")propertyQuery=propertyQuery.Where(x=>x.ArchivedAtUtc!=null);
        else if(recordState!="all")return Results.ValidationProblem(new Dictionary<string,string[]>{{"recordState",["Use active, draft, archived ou all."]}});
        if(http.User.IsInRole(SystemRoles.HealthAgent))
        {
            var assignedMicroregions=await db.Microregions.AsNoTracking().Where(x=>x.AssignedAgentId==actor.Id&&x.ArchivedAtUtc==null).Select(x=>x.Id).ToArrayAsync();
            propertyQuery=propertyQuery.Where(x=>assignedMicroregions.Contains(x.MicroregionId));
        }
        var properties=await propertyQuery.OrderBy(x=>x.Street).ThenBy(x=>x.HouseNumber).ToListAsync();
        if(microregionId.HasValue)properties=properties.Where(x=>x.MicroregionId==microregionId).ToList();
        if(Enum.TryParse<PropertyRegistrationStatus>(registrationStatus,true,out var status))properties=properties.Where(x=>x.RegistrationStatus==status).ToList();
        var currentFamilies=await FamilyAccess.CurrentByPropertyAsync(properties.Select(p=>p.Id),db);
        var canSeeFamilies=FamilyAccess.Has(http.User,StuPermissions.FamiliesView);
        if(!string.IsNullOrWhiteSpace(query)){var q=query.Trim();properties=properties.Where(x=>x.Id.ToString()==q||x.Street.Contains(q,StringComparison.OrdinalIgnoreCase)||x.HouseNumber.Contains(q,StringComparison.OrdinalIgnoreCase)||(canSeeFamilies&&currentFamilies.TryGetValue(x.Id,out var f)&&(f.Number.Contains(q,StringComparison.OrdinalIgnoreCase)||f.ResponsibleName.Contains(q,StringComparison.OrdinalIgnoreCase)))).ToList();}
        var ids=properties.Select(x=>x.Id).ToArray();
        var rules=await db.CoverageRules.AsNoTracking().Where(x=>x.HealthUnitId==scope.UnitId).ToDictionaryAsync(x=>x.MicroregionId,x=>x.MaxDaysWithoutVisit);var tags=await LoadTagsAsync(ids,db);
        var projected=properties.Select(x=>Response(x,currentFamilies.GetValueOrDefault(x.Id),rules.GetValueOrDefault(x.MicroregionId),tags.GetValueOrDefault(x.Id,[]),canSeeFamilies)).ToList();
        var coverageSummary=new { total=projected.Count(x=>x.CoverageStatus is not ("noFamily" or "restricted")), noFamily=projected.Count(x=>x.CoverageStatus=="noFamily"), overdue=projected.Count(x=>x.CoverageStatus=="overdue"), neverVisited=projected.Count(x=>x.CoverageStatus=="neverVisited"), covered=projected.Count(x=>x.CoverageStatus=="covered"), notConfigured=projected.Count(x=>x.CoverageStatus=="notConfigured") };
        if(coverage=="pending")projected=projected.Where(x=>x.CoverageStatus is "overdue" or "neverVisited").ToList();
        else if(!string.IsNullOrWhiteSpace(coverage))projected=projected.Where(x=>string.Equals(x.CoverageStatus,coverage,StringComparison.OrdinalIgnoreCase)).ToList();
        return Results.Ok(new{items=projected.Skip((page-1)*pageSize).Take(pageSize),total=projected.Count,page,pageSize,coverageSummary});
    }

    private static async Task<IResult> GetPropertyMapAsync(Guid? healthUnitId,HttpContext http,UserManager<ApplicationUser> users,StuDbContext db)
    {
        var actor=await ActorAsync(http,users);var scope=Scope(actor,http.User,healthUnitId);if(scope.Error is not null)return scope.Error;
        var items=await db.Properties.AsNoTracking().Where(x=>x.HealthUnitId==scope.UnitId&&x.ArchivedAtUtc==null&&x.RegistrationStatus==PropertyRegistrationStatus.Active).ToListAsync();
        if(http.User.IsInRole(SystemRoles.HealthAgent))
        {
            var assignedMicroregions=await db.Microregions.AsNoTracking().Where(x=>x.AssignedAgentId==actor.Id&&x.ArchivedAtUtc==null).Select(x=>x.Id).ToArrayAsync();
            items=items.Where(x=>assignedMicroregions.Contains(x.MicroregionId)).ToList();
        }
        var families=await FamilyAccess.CurrentByPropertyAsync(items.Select(p=>p.Id),db);
        var canSeeFamilies=FamilyAccess.Has(http.User,StuPermissions.FamiliesView);
        var rules=await db.CoverageRules.AsNoTracking().Where(x=>x.HealthUnitId==scope.UnitId).ToDictionaryAsync(x=>x.MicroregionId,x=>x.MaxDaysWithoutVisit);
        return Results.Ok(new{type="FeatureCollection",features=items.Select(x=> {var family=families.GetValueOrDefault(x.Id);return new{type="Feature",id=x.Id,geometry=x.Geometry,properties=new{entityType="property",id=x.Id,x.HouseNumber,x.MicroregionId,familyId=canSeeFamilies?family?.Id:null,familyNumber=canSeeFamilies?family?.Number:null,familyResponsibleName=canSeeFamilies?family?.ResponsibleName:null,coverageStatus=family is null?"noFamily":canSeeFamilies?FamilyAccess.Coverage(family.LastVisitAtUtc,rules.GetValueOrDefault(x.MicroregionId)):"restricted"}};})});
    }

    private static async Task<IResult> GetReferenceDataAsync(Guid? healthUnitId,HttpContext http,UserManager<ApplicationUser> users,StuDbContext db)
    {
        var actor=await ActorAsync(http,users);var scope=Scope(actor,http.User,healthUnitId);if(scope.Error is not null)return scope.Error;
        var micro=await db.Microregions.AsNoTracking().Where(x=>x.HealthUnitId==scope.UnitId&&x.ArchivedAtUtc==null).OrderBy(x=>x.Code).Select(x=>new{x.Id,x.Code,x.Name,x.AssignedAgentId,boundary=x.Boundary}).ToListAsync();
        if(http.User.IsInRole(SystemRoles.HealthAgent))micro=micro.Where(x=>x.AssignedAgentId==actor.Id).ToList();
        var tags=await db.OperationalTags.AsNoTracking().Where(x=>x.HealthUnitId==scope.UnitId&&x.ArchivedAtUtc==null).OrderBy(x=>x.Name).Select(x=>new{x.Id,x.Name,x.Color}).ToListAsync();
        var rules=await db.CoverageRules.AsNoTracking().Where(x=>x.HealthUnitId==scope.UnitId).Select(x=>new{x.MicroregionId,x.MaxDaysWithoutVisit}).ToListAsync();
        return Results.Ok(new{selectedHealthUnitId=scope.UnitId,microregions=micro,tags,coverageRules=rules});
    }

    private static async Task<IResult> GetPropertyAsync(Guid id,HttpContext http,UserManager<ApplicationUser> users,StuDbContext db)
    {
        var item=await db.Properties.AsNoTracking().SingleOrDefaultAsync(x=>x.Id==id);if(item is null)return Results.NotFound();var actor=await ActorAsync(http,users);if(!await CanViewAsync(item,actor,http.User,db))return Results.Forbid();
        var tags=await LoadTagsAsync([id],db);var families=await FamilyAccess.CurrentByPropertyAsync([id],db);
        var days=await db.CoverageRules.AsNoTracking().Where(x=>x.MicroregionId==item.MicroregionId).Select(x=>(int?)x.MaxDaysWithoutVisit).SingleOrDefaultAsync();
        return Results.Ok(Response(item,families.GetValueOrDefault(id),days??0,tags.GetValueOrDefault(id,[]),FamilyAccess.Has(http.User,StuPermissions.FamiliesView)));
    }

    private static async Task<IResult> GetOccupancyAsync(Guid id,HttpContext http,UserManager<ApplicationUser> users,StuDbContext db)
    {
        var item=await db.Properties.AsNoTracking().SingleOrDefaultAsync(p=>p.Id==id);if(item is null)return Results.NotFound();
        if(!await CanViewAsync(item,await ActorAsync(http,users),http.User,db))return Results.Forbid();
        return Results.Ok(await(from l in db.FamilyPropertyLinks.AsNoTracking() join f in db.Families.AsNoTracking() on l.FamilyId equals f.Id where l.PropertyId==id orderby l.StartedAtUtc descending select new{l.Id,l.FamilyId,f.Number,f.ResponsibleName,l.StartedAtUtc,l.EndedAtUtc,l.EndReason}).ToListAsync());
    }

    private static async Task<IResult> GetVersionsAsync(Guid id,HttpContext http,UserManager<ApplicationUser> users,StuDbContext db){var item=await db.Properties.AsNoTracking().SingleOrDefaultAsync(x=>x.Id==id);if(item is null)return Results.NotFound();var actor=await ActorAsync(http,users);if(!await CanViewAsync(item,actor,http.User,db))return Results.Forbid();var versions=await db.PropertyVersions.AsNoTracking().Where(x=>x.PropertyId==id).OrderByDescending(x=>x.VersionNumber).Select(x=>new{x.VersionNumber,x.ChangeKind,x.ChangedAtUtc,x.ChangedByUserId,x.HouseNumber,x.Street,x.MicroregionId,x.RegistrationStatus,x.Situation,x.IsArchived,geometry=x.Geometry}).ToListAsync();return Results.Ok(versions);}

    private static async Task<IResult> CreatePropertyAsync(SavePropertyRequest request,HttpContext http,UserManager<ApplicationUser> users,StuDbContext db)
    {
        var actor=await ActorAsync(http,users);var validation=await ValidatePropertyAsync(request,null,actor,http.User,users,db);if(validation.Error is not null)return validation.Error;
        var item=HealthProperty.Create(validation.UnitId,request.MicroregionId,request.Street,request.HouseNumber,request.PostalCode,request.Complement,validation.Geometry!,validation.Status,validation.Situation);db.Properties.Add(item);await SyncTagsAsync(item.Id,validation.UnitId,request.TagIds,db);db.PropertyVersions.Add(PropertyVersion.Capture(item,1,"Create",actor.Id));Audit(db,http,actor,"Create","Property",item.Id,$"Imóvel {item.HouseNumber}, criado.");
        await db.SaveChangesAsync();return Results.Created($"/api/properties/{item.Id}",new{item.Id,item.ConcurrencyToken});
    }

    private static async Task<IResult> UpdatePropertyAsync(Guid id,SavePropertyRequest request,HttpContext http,UserManager<ApplicationUser> users,StuDbContext db)
    {
        var item=await db.Properties.SingleOrDefaultAsync(x=>x.Id==id);if(item is null)return Results.NotFound();var actor=await ActorAsync(http,users);if(!await CanManageAsync(item,actor,http.User,users,db))return Results.Forbid();if(request.ExpectedVersion!=item.ConcurrencyToken)return Conflict("Cadastro alterado","Recarregue o imóvel antes de editar novamente.");
        var validation=await ValidatePropertyAsync(request,id,actor,http.User,users,db);if(validation.Error is not null)return validation.Error;
        if(validation.UnitId!=item.HealthUnitId)return Conflict("Transferência de UBS indisponível","A edição do imóvel permite alterar a microrregião dentro da mesma UBS. A transferência entre UBS exige um fluxo que preserve visitas, etiquetas e histórico.");
        if(validation.Status!=PropertyRegistrationStatus.Active && await db.FamilyPropertyLinks.AnyAsync(l=>l.PropertyId==id&&l.EndedAtUtc==null))return Conflict("Imóvel com família","Encerre o vínculo antes de mudar o imóvel para rascunho.");
        var reassigned=!string.Equals(item.HouseNumber,request.HouseNumber.Trim(),StringComparison.OrdinalIgnoreCase);
        item.Update(request.MicroregionId,request.Street,request.HouseNumber,request.PostalCode,request.Complement,validation.Geometry!,validation.Status,validation.Situation);await SyncTagsAsync(item.Id,item.HealthUnitId,request.TagIds,db);var kind=reassigned?"ReassignIdentifiers":"Update";db.PropertyVersions.Add(PropertyVersion.Capture(item,await NextVersionAsync(id,db),kind,actor.Id));Audit(db,http,actor,kind,"Property",item.Id,$"Imóvel {item.HouseNumber}, atualizado.");await db.SaveChangesAsync();return Results.Ok(new{item.ConcurrencyToken});
    }

    private static Task<IResult> ArchivePropertyAsync(Guid id,HttpContext http,UserManager<ApplicationUser> users,StuDbContext db)=>SetArchivedAsync(id,true,http,users,db);
    private static Task<IResult> RestorePropertyAsync(Guid id,HttpContext http,UserManager<ApplicationUser> users,StuDbContext db)=>SetArchivedAsync(id,false,http,users,db);
    private static async Task<IResult> SetArchivedAsync(Guid id,bool archive,HttpContext http,UserManager<ApplicationUser> users,StuDbContext db)
    {
        var item=await db.Properties.SingleOrDefaultAsync(x=>x.Id==id);
        if(item is null)return Results.NotFound();
        var actor=await ActorAsync(http,users);
        if(!await CanManageAsync(item,actor,http.User,users,db))return Results.Forbid();
        if(!archive)
        {
            var micro=await db.Microregions.AsNoTracking().SingleOrDefaultAsync(x=>x.Id==item.MicroregionId&&x.ArchivedAtUtc==null);
            if(micro is null||micro.HealthUnitId!=item.HealthUnitId||!micro.Boundary.Covers(item.Geometry))return Conflict("Vínculo territorial inválido","Edite o imóvel para posicioná-lo dentro de uma microrregião ativa da mesma UBS antes de reativar.");
            if(!await db.HealthUnits.AnyAsync(x=>x.Id==item.HealthUnitId&&x.ArchivedAtUtc==null))return Conflict("UBS indisponível","Reative a UBS antes de reativar o imóvel.");
        }
        if(archive && await db.FamilyPropertyLinks.AnyAsync(l=>l.PropertyId==id && l.EndedAtUtc==null))return Conflict("Imóvel com família","Encerre o vínculo residencial antes de arquivar o imóvel.");
        if(archive)item.Archive();else item.Restore();
        var action=archive?"Archive":"Restore";
        db.PropertyVersions.Add(PropertyVersion.Capture(item,await NextVersionAsync(id,db),action,actor.Id));
        Audit(db,http,actor,action,"Property",item.Id,$"Imóvel {(archive?"arquivado":"reativado")}.");
        await db.SaveChangesAsync();return Results.NoContent();
    }

    private static async Task<IResult> GetVisitsAsync(Guid propertyId,HttpContext http,UserManager<ApplicationUser> users,StuDbContext db){var property=await db.Properties.AsNoTracking().SingleOrDefaultAsync(x=>x.Id==propertyId);if(property is null)return Results.NotFound();var actor=await ActorAsync(http,users);if(!await CanViewAsync(property,actor,http.User,db))return Results.Forbid();var visits=await(from visit in db.PropertyVisits.AsNoTracking() join user in db.Users.AsNoTracking() on visit.AgentId equals user.Id where visit.PropertyId==propertyId orderby visit.VisitedAtUtc descending select new{visit.Id,visit.FamilyId,visit.PropertyId,visit.VisitedAtUtc,visit.Type,visit.Outcome,visit.ObservedSituation,visit.AccessDifficulty,visit.Note,visit.ArchivedAtUtc,visit.ConcurrencyToken,agentId=visit.AgentId,agentName=user.DisplayName}).ToListAsync();return Results.Ok(visits);}
    private static async Task<IResult> GetTagsAsync(Guid healthUnitId,HttpContext http,UserManager<ApplicationUser> users,StuDbContext db){var actor=await ActorAsync(http,users);var scope=Scope(actor,http.User,healthUnitId);if(scope.Error is not null)return scope.Error;return Results.Ok(await db.OperationalTags.AsNoTracking().Where(x=>x.HealthUnitId==scope.UnitId).OrderBy(x=>x.Name).Select(x=>new{x.Id,x.Name,x.Color,x.ArchivedAtUtc}).ToListAsync());}
    private static async Task<IResult> CreateTagAsync(SaveTagRequest request,HttpContext http,UserManager<ApplicationUser> users,StuDbContext db){var actor=await ActorAsync(http,users);var scope=Scope(actor,http.User,request.HealthUnitId);if(scope.Error is not null)return scope.Error;var error=ValidateTag(request.Name,request.Color);if(error is not null)return error;if(await db.OperationalTags.AnyAsync(x=>x.HealthUnitId==scope.UnitId&&EF.Functions.ILike(x.Name,request.Name.Trim())))return Conflict("Tag existente","Já existe uma tag com este nome.");var tag=OperationalTag.Create(scope.UnitId,request.Name,request.Color);db.OperationalTags.Add(tag);Audit(db,http,actor,"Create","OperationalTag",tag.Id,$"Tag {tag.Name} criada.");await db.SaveChangesAsync();return Results.Created($"/api/property-settings/tags/{tag.Id}",new{tag.Id});}
    private static async Task<IResult> UpdateTagAsync(Guid id,SaveTagRequest request,HttpContext http,UserManager<ApplicationUser> users,StuDbContext db){var tag=await db.OperationalTags.SingleOrDefaultAsync(x=>x.Id==id);if(tag is null)return Results.NotFound();var actor=await ActorAsync(http,users);var scope=Scope(actor,http.User,tag.HealthUnitId);if(scope.Error is not null)return scope.Error;var error=ValidateTag(request.Name,request.Color);if(error is not null)return error;if(await db.OperationalTags.AnyAsync(x=>x.Id!=id&&x.HealthUnitId==tag.HealthUnitId&&EF.Functions.ILike(x.Name,request.Name.Trim())))return Conflict("Tag existente","Já existe uma tag com este nome.");tag.Update(request.Name,request.Color);Audit(db,http,actor,"Update","OperationalTag",tag.Id,$"Tag {tag.Name} atualizada.");await db.SaveChangesAsync();return Results.NoContent();}
    private static async Task<IResult> ArchiveTagAsync(Guid id,HttpContext http,UserManager<ApplicationUser> users,StuDbContext db){var tag=await db.OperationalTags.SingleOrDefaultAsync(x=>x.Id==id);if(tag is null)return Results.NotFound();var actor=await ActorAsync(http,users);var scope=Scope(actor,http.User,tag.HealthUnitId);if(scope.Error is not null)return scope.Error;tag.Archive();Audit(db,http,actor,"Archive","OperationalTag",tag.Id,$"Tag {tag.Name} arquivada.");await db.SaveChangesAsync();return Results.NoContent();}
    private static async Task<IResult> RestoreTagAsync(Guid id,HttpContext http,UserManager<ApplicationUser> users,StuDbContext db){var tag=await db.OperationalTags.SingleOrDefaultAsync(x=>x.Id==id);if(tag is null)return Results.NotFound();var actor=await ActorAsync(http,users);var scope=Scope(actor,http.User,tag.HealthUnitId);if(scope.Error is not null)return scope.Error;tag.Restore();Audit(db,http,actor,"Restore","OperationalTag",tag.Id,$"Tag {tag.Name} reativada.");await db.SaveChangesAsync();return Results.NoContent();}
    private static async Task<IResult> SaveCoverageRuleAsync(Guid microregionId,SaveCoverageRuleRequest request,HttpContext http,UserManager<ApplicationUser> users,StuDbContext db){if(request.MaxDaysWithoutVisit is<1 or>730)return Validation("maxDaysWithoutVisit","Informe um período entre 1 e 730 dias.");var micro=await db.Microregions.SingleOrDefaultAsync(x=>x.Id==microregionId&&x.ArchivedAtUtc==null);if(micro is null)return Results.NotFound();var actor=await ActorAsync(http,users);var scope=Scope(actor,http.User,micro.HealthUnitId);if(scope.Error is not null)return scope.Error;var rule=await db.CoverageRules.SingleOrDefaultAsync(x=>x.MicroregionId==microregionId);if(rule is null){rule=CoverageRule.Create(scope.UnitId,microregionId,request.MaxDaysWithoutVisit);db.CoverageRules.Add(rule);}else rule.Update(request.MaxDaysWithoutVisit);Audit(db,http,actor,"Update","CoverageRule",rule.Id,$"Prazo de cobertura alterado para {rule.MaxDaysWithoutVisit} dias.");await db.SaveChangesAsync();return Results.NoContent();}

    private static async Task<PropertyValidation> ValidatePropertyAsync(SavePropertyRequest request,Guid? currentId,ApplicationUser actor,System.Security.Claims.ClaimsPrincipal principal,UserManager<ApplicationUser> users,StuDbContext db)
    {
        if(string.IsNullOrWhiteSpace(request.Street)||request.Street.Trim().Length>180)return PropertyValidation.Fail(Validation("street","Informe um logradouro de até 180 caracteres."));if(string.IsNullOrWhiteSpace(request.HouseNumber)||request.HouseNumber.Trim().Length>32)return PropertyValidation.Fail(Validation("houseNumber","Informe o número do imóvel."));
        if(!Enum.TryParse<PropertyRegistrationStatus>(request.RegistrationStatus,true,out var status)||!Enum.IsDefined(status))return PropertyValidation.Fail(Validation("registrationStatus","Situação cadastral inválida."));if(!Enum.TryParse<PropertySituation>(request.Situation,true,out var situation)||!Enum.IsDefined(situation))return PropertyValidation.Fail(Validation("situation","Situação do imóvel inválida."));
        var parsed=ParseGeometry(request.Geometry);if(parsed.Error is not null)return PropertyValidation.Fail(parsed.Error);var micro=await db.Microregions.AsNoTracking().SingleOrDefaultAsync(x=>x.Id==request.MicroregionId&&x.ArchivedAtUtc==null);if(micro is null)return PropertyValidation.Fail(Validation("microregionId","Selecione uma microrregião ativa."));var scope=Scope(actor,principal,micro.HealthUnitId);if(scope.Error is not null)return PropertyValidation.Fail(scope.Error);if(principal.IsInRole(SystemRoles.HealthAgent)&&micro.AssignedAgentId!=actor.Id)return PropertyValidation.Fail(Results.Forbid());if(!micro.Boundary.Covers(parsed.Geometry!))return PropertyValidation.Fail(Validation("geometry","A posição do imóvel precisa estar dentro da microrregião."));
        var distinctTags=request.TagIds.Distinct().ToArray();if(distinctTags.Length!=request.TagIds.Length||await db.OperationalTags.CountAsync(x=>distinctTags.Contains(x.Id)&&x.HealthUnitId==scope.UnitId&&x.ArchivedAtUtc==null)!=distinctTags.Length)return PropertyValidation.Fail(Validation("tagIds","Selecione somente tags ativas da UBS."));return new(null,parsed.Geometry,scope.UnitId,status,situation);
    }

    private static GeometryParse ParseGeometry(JsonElement element){try{var geometry=JsonSerializer.Deserialize<Geometry>(element.GetRawText(),GeometryJsonOptions);if(geometry is null||geometry.IsEmpty)return GeometryParse.Fail(Validation("geometry","Informe a posição ou contorno do imóvel."));geometry.SRID=4326;if(geometry is not(Point or Polygon or MultiPolygon)||!geometry.IsValid)return GeometryParse.Fail(Validation("geometry","Use um ponto, polígono ou multipolígono válido."));var e=geometry.EnvelopeInternal;if(e.MinX< -180||e.MaxX>180||e.MinY< -90||e.MaxY>90)return GeometryParse.Fail(Validation("geometry","Use coordenadas longitude/latitude válidas."));return new(null,geometry);}catch(JsonException){return GeometryParse.Fail(Validation("geometry","GeoJSON inválido."));}}
    internal static IResult? ValidateVisit(SaveVisitRequest request,out VisitType type,out VisitOutcome outcome,out PropertySituation situation){type=default;outcome=default;situation=default;if(request.VisitedAtUtc>DateTimeOffset.UtcNow.AddMinutes(5)||request.VisitedAtUtc<DateTimeOffset.UtcNow.AddYears(-10))return Validation("visitedAtUtc","Informe uma data de visita válida.");if(!Enum.TryParse(request.Type,true,out type)||!Enum.IsDefined(type))return Validation("type","Tipo de visita inválido.");if(!Enum.TryParse(request.Outcome,true,out outcome)||!Enum.IsDefined(outcome))return Validation("outcome","Resultado da visita inválido.");if(!Enum.TryParse(request.ObservedSituation,true,out situation)||!Enum.IsDefined(situation))return Validation("observedSituation","Situação observada inválida.");if(request.Note?.Trim().Length>240)return Validation("note","A observação pode ter até 240 caracteres.");if(!string.IsNullOrWhiteSpace(request.Note)&&(CpfRegex().IsMatch(request.Note)||EmailRegex().IsMatch(request.Note)||PhoneRegex().IsMatch(request.Note)))return Validation("note","Não registre CPF, telefone, e-mail ou outros dados pessoais na observação.");return null;}
    private static IResult? ValidateTag(string name,string color){if(string.IsNullOrWhiteSpace(name)||name.Trim().Length>80)return Validation("name","Informe um nome de até 80 caracteres.");if(!ColorRegex().IsMatch(color??string.Empty))return Validation("color","Informe uma cor hexadecimal, como #4f9a7d.");return null;}
    private static async Task<bool> CanViewAsync(HealthProperty item,ApplicationUser actor,System.Security.Claims.ClaimsPrincipal principal,StuDbContext db){if(principal.IsInRole(SystemRoles.GlobalAdministrator))return true;if(actor.HealthUnitId!=item.HealthUnitId)return false;if(principal.IsInRole(SystemRoles.HealthAgent))return await db.Microregions.AnyAsync(x=>x.Id==item.MicroregionId&&x.AssignedAgentId==actor.Id&&x.ArchivedAtUtc==null);return true;}
    private static async Task<bool> CanManageAsync(HealthProperty item,ApplicationUser actor,System.Security.Claims.ClaimsPrincipal principal,UserManager<ApplicationUser> users,StuDbContext db)=>await CanViewAsync(item,actor,principal,db)&&(principal.IsInRole(SystemRoles.GlobalAdministrator)||!await users.IsInRoleAsync(actor,SystemRoles.HealthAgent)||await db.Microregions.AnyAsync(x=>x.Id==item.MicroregionId&&x.AssignedAgentId==actor.Id));
    private static ScopeResult Scope(ApplicationUser actor,System.Security.Claims.ClaimsPrincipal principal,Guid? requested){if(principal.IsInRole(SystemRoles.GlobalAdministrator))return requested.HasValue?new(null,requested.Value):new(Validation("healthUnitId","Selecione uma UBS."),Guid.Empty);if(!actor.HealthUnitId.HasValue)return new(Results.Forbid(),Guid.Empty);if(requested.HasValue&&requested!=actor.HealthUnitId)return new(Results.Forbid(),Guid.Empty);return new(null,actor.HealthUnitId.Value);}
    private static async Task<Dictionary<Guid,List<object>>> LoadTagsAsync(Guid[] propertyIds,StuDbContext db){var rows=await(from link in db.PropertyTags.AsNoTracking() join tag in db.OperationalTags.AsNoTracking() on link.TagId equals tag.Id where propertyIds.Contains(link.PropertyId) select new{link.PropertyId,tag.Id,tag.Name,tag.Color}).ToListAsync();return rows.GroupBy(x=>x.PropertyId).ToDictionary(g=>g.Key,g=>g.Select(x=>(object)new{x.Id,x.Name,x.Color}).ToList());}
    private static async Task SyncTagsAsync(Guid propertyId,Guid unitId,Guid[] tagIds,StuDbContext db){var current=await db.PropertyTags.Where(x=>x.PropertyId==propertyId).ToListAsync();db.PropertyTags.RemoveRange(current.Where(x=>!tagIds.Contains(x.TagId)));var existing=current.Select(x=>x.TagId).ToHashSet();db.PropertyTags.AddRange(tagIds.Where(id=>!existing.Contains(id)).Select(id=>PropertyTag.Create(propertyId,id)));}
    private static PropertyResponse Response(HealthProperty x,FamilyAccess.CurrentFamily? family,int days,List<object> tags,bool authorized)=>new(x.Id,x.HealthUnitId,x.MicroregionId,x.Street,x.HouseNumber,authorized?family?.Number:null,x.PostalCode,x.Complement,x.Geometry,x.RegistrationStatus.ToString(),x.Situation.ToString(),x.ConcurrencyToken,x.ArchivedAtUtc,authorized?family?.LastVisitAtUtc:null,family is null?"noFamily":authorized?FamilyAccess.Coverage(family.LastVisitAtUtc,days):"restricted",tags,authorized?family?.Id:null,authorized?family?.ResponsibleName:null,authorized?family?.ConcurrencyToken:null);
    private static async Task<int> NextVersionAsync(Guid id,StuDbContext db)=>(await db.PropertyVersions.Where(x=>x.PropertyId==id).MaxAsync(x=>(int?)x.VersionNumber)??0)+1;
    private static async Task<ApplicationUser> ActorAsync(HttpContext http,UserManager<ApplicationUser> users)=>await users.GetUserAsync(http.User)??throw new InvalidOperationException("Usuário autenticado não encontrado.");
    private static void Audit(StuDbContext db,HttpContext http,ApplicationUser actor,string action,string entity,Guid id,string summary)=>db.AuditEntries.Add(AuditEntry.Create(actor.Id,actor.UserName??actor.DisplayName,action,entity,id.ToString(),summary,null,null,http.Connection.RemoteIpAddress?.ToString()));
    private static IResult Validation(string field,string message)=>Results.ValidationProblem(new Dictionary<string,string[]>{{field,[message]}});private static IResult Conflict(string title,string detail)=>Results.Problem(title:title,detail:detail,statusCode:409);
    private static JsonSerializerOptions CreateGeometryOptions(){var o=new JsonSerializerOptions(JsonSerializerDefaults.Web);o.Converters.Add(new GeoJsonConverterFactory());return o;}
    [GeneratedRegex(@"\b\d{3}\.?\d{3}\.?\d{3}-?\d{2}\b")]private static partial Regex CpfRegex();
    [GeneratedRegex(@"\b[\w.+-]+@[\w.-]+\.[A-Za-z]{2,}\b",RegexOptions.IgnoreCase)]private static partial Regex EmailRegex();
    [GeneratedRegex(@"(?:\+?55\s*)?(?:\(?\d{2}\)?\s*)?\d{4,5}[-\s]?\d{4}")]private static partial Regex PhoneRegex();
    [GeneratedRegex("^#[0-9a-fA-F]{6}$")]private static partial Regex ColorRegex();
    private sealed record ScopeResult(IResult? Error,Guid UnitId);private sealed record GeometryParse(IResult? Error,Geometry? Geometry){public static GeometryParse Fail(IResult e)=>new(e,null);}private sealed record PropertyValidation(IResult? Error,Geometry? Geometry,Guid UnitId,PropertyRegistrationStatus Status,PropertySituation Situation){public static PropertyValidation Fail(IResult e)=>new(e,null,Guid.Empty,default,default);}
    private sealed record PropertyResponse(Guid Id,Guid HealthUnitId,Guid MicroregionId,string Street,string HouseNumber,string? FamilyNumber,string? PostalCode,string? Complement,Geometry Geometry,string RegistrationStatus,string Situation,Guid ConcurrencyToken,DateTimeOffset? ArchivedAtUtc,DateTimeOffset? LastVisitAtUtc,string CoverageStatus,List<object> Tags,Guid? FamilyId,string? FamilyResponsibleName,Guid? FamilyConcurrencyToken);
}
