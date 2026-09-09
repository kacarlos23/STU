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
      <p role="status">
        {impact.affectedPropertyCount} imóvel(is) afetado(s).
        {impact.valid ? " Alteração disponível para salvar." : " Revise os impedimentos antes de salvar."}
      </p>
      {impact.conflicts.map((message) => <p className="territory-error" key={message}>{message}</p>)}
      {impact.affectedProperties.length > 0 && (
        <ol>
          {impact.affectedProperties.map((property) => (
            <li key={property.id}>
              <strong>{property.street}, nº {property.houseNumber} · Família {property.familyNumber}</strong>
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
