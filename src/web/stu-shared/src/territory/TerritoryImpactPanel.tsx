export type TerritoryImpact = {
  valid: boolean;
  affectedPropertyCount: number;
  blockedPropertyCount: number;
  conflicts: string[];
  propertyDetailsVisible?: boolean;
  affectedProperties: {
    id: string;
    street: string;
    houseNumber: string;
    familyNumber: string;
    reason: string;
  }[];
};

export function TerritoryImpactPanel({ impact }: { impact: TerritoryImpact }) {
  return (
    <section className="territory-impact" aria-label="Resultado da validação de impacto">
      <header><div><span>Prévia antes de salvar</span><h4>{impact.valid ? "Impacto conferido" : "A alteração possui impedimentos"}</h4><p>Nenhuma alteração foi gravada no território.</p></div><strong>{impact.valid ? "Validado" : "Bloqueado"}</strong></header>
      <div className="territory-impact-counts"><span><small>Imóveis afetados</small><b>{impact.affectedPropertyCount}</b></span><span><small>Impedimentos</small><b>{impact.blockedPropertyCount}</b></span></div>
      <p role="status">{impact.valid ? "Alteração disponível para salvar. Ela será versionada na auditoria." : "Revise os impedimentos antes de salvar."}</p>
      {impact.conflicts.map((message) => <p className="territory-error" key={message}>{message}</p>)}
      {impact.affectedProperties.length > 0 && (
        <ol>
          {impact.affectedProperties.map((property) => (
            <li key={property.id}>
              <strong>{property.street}, nº {property.houseNumber}</strong>
              <span>{property.reason}</span>
            </li>
          ))}
        </ol>
      )}
      {impact.propertyDetailsVisible === false && impact.affectedPropertyCount > 0 && <p>A consulta dos detalhes dos imóveis exige permissão de acesso aos cadastros.</p>}
      {impact.propertyDetailsVisible !== false && impact.affectedPropertyCount > impact.affectedProperties.length && (
        <p>Exibindo os primeiros {impact.affectedProperties.length} de {impact.affectedPropertyCount} imóveis afetados.</p>
      )}
    </section>
  );
}
