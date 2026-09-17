import { createRoot } from "react-dom/client";
import { TerritoryWorkspace } from "@stu/shared/territory";
import { InteractionProvider, type Session } from "@stu/shared";
import "../../styles/global.css";

const healthUnit = {
  id: "visual-unit",
  code: "UBS-01",
  name: "UBS de validação",
};
const geometry = {
  type: "Polygon" as const,
  coordinates: [
    [
      [-39.76402338263364, -17.528398349197232],
      [-39.76970499396268, -17.536539741473263],
      [-39.765232691784945, -17.538036744020076],
      [-39.76262738481378, -17.54023775363126],
      [-39.762109717959504, -17.538311901200174],
      [-39.76140535158444, -17.53806914523996],
      [-39.76107438425103, -17.538845963167603],
      [-39.75960608475168, -17.538352205706857],
      [-39.7602510467342, -17.53655580430444],
      [-39.75780971453926, -17.53053697057483],
      [-39.76068658442159, -17.52955781092983],
      [-39.76402338263364, -17.528398349197232],
    ],
  ],
};

const session: Session = {
  id: "visual-user",
  userName: "validacao",
  displayName: "Validação visual",
  mustChangePassword: false,
  healthUnit,
  roles: [{ name: "manager", displayName: "Gerente" }],
  permissions: ["territory.manage"],
};

const originalFetch = globalThis.fetch.bind(globalThis);
globalThis.fetch = async (input, init) => {
  const url =
    typeof input === "string"
      ? input
      : input instanceof URL
        ? input.href
        : input.url;
  if (url.startsWith("/api/territories/map")) {
    return Response.json({
      type: "FeatureCollection",
      features: [
        {
          type: "Feature",
          id: "jardim-liberdade",
          geometry,
          properties: {
            entityType: "neighborhood",
            id: "jardim-liberdade",
            name: "Jardim Liberdade",
            source: "Manual",
            concurrencyToken: "visual-version",
          },
        },
      ],
    });
  }
  if (url.startsWith("/api/territories/reference-data")) {
    return Response.json({
      selectedHealthUnitId: healthUnit.id,
      neighborhoods: [
        {
          id: "jardim-liberdade",
          name: "Jardim Liberdade",
          source: "Manual",
          externalReference: null,
          geometry,
          concurrencyToken: "visual-version",
        },
      ],
      agents: [],
      healthUnits: [healthUnit],
    });
  }
  return originalFetch(input, init);
};

createRoot(document.getElementById("root")!).render(
  <InteractionProvider><TerritoryWorkspace session={session} /></InteractionProvider>,
);
