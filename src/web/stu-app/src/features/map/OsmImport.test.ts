import { describe, expect, it } from "vitest";
import { parseOsmAreas } from "@stu/shared/territory";

describe("importação de mapas OSM", () => {
  it("oferece o retângulo completo selecionado na exportação do OpenStreetMap", () => {
    const candidates = parseOsmAreas(`
      <osm version="0.6">
        <bounds minlat="-17.6" minlon="-39.8" maxlat="-17.5" maxlon="-39.7" />
      </osm>
    `);

    expect(candidates).toHaveLength(1);
    expect(candidates[0]).toMatchObject({
      id: "bounds",
      label: "Área completa exportada",
      externalReference: "OSM bounds -39.8,-17.6,-39.7,-17.5",
      geometry: { type: "Polygon" },
    });
  });

  it("detecta uma área territorial fechada representada por way", () => {
    const candidates = parseOsmAreas(`
      <osm version="0.6">
        <node id="1" lat="-17.55" lon="-39.76" />
        <node id="2" lat="-17.55" lon="-39.74" />
        <node id="3" lat="-17.53" lon="-39.74" />
        <way id="100">
          <nd ref="1"/><nd ref="2"/><nd ref="3"/><nd ref="1"/>
          <tag k="boundary" v="administrative"/>
          <tag k="name" v="Bairro Teste"/>
        </way>
      </osm>
    `);

    expect(candidates).toHaveLength(1);
    expect(candidates[0]).toMatchObject({
      id: "way/100",
      label: "Bairro Teste",
      externalReference: "OSM way/100",
      geometry: { type: "Polygon" },
    });
  });

  it("monta uma relação multipolígono cujos limites estão fragmentados", () => {
    const candidates = parseOsmAreas(`
      <osm version="0.6">
        <node id="1" lat="-17.55" lon="-39.76" />
        <node id="2" lat="-17.55" lon="-39.74" />
        <node id="3" lat="-17.53" lon="-39.74" />
        <node id="4" lat="-17.53" lon="-39.76" />
        <way id="10"><nd ref="1"/><nd ref="2"/><nd ref="3"/></way>
        <way id="11"><nd ref="1"/><nd ref="4"/><nd ref="3"/></way>
        <relation id="200">
          <member type="way" ref="10" role="outer"/>
          <member type="way" ref="11" role="outer"/>
          <tag k="type" v="multipolygon"/>
          <tag k="boundary" v="administrative"/>
          <tag k="name" v="Microrregião Teste"/>
        </relation>
      </osm>
    `);

    expect(candidates).toHaveLength(1);
    expect(candidates[0]).toMatchObject({
      id: "relation/200",
      label: "Microrregião Teste",
      externalReference: "OSM relation/200",
      geometry: { type: "Polygon" },
    });
    const coordinates = candidates[0].geometry.coordinates as number[][][];
    expect(coordinates[0][0]).toEqual(coordinates[0].at(-1));
  });

  it("rejeita declarações XML capazes de carregar entidades externas", () => {
    expect(() =>
      parseOsmAreas(
        '<!DOCTYPE osm [<!ENTITY xxe SYSTEM "file:///etc/passwd">]><osm>&xxe;</osm>',
      ),
    ).toThrow("declarações XML não permitidas");
  });
});
