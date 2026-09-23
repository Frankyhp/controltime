// Regenera el icono .ico multi-tamaño de ControlTime a partir del SVG fuente.
//
// Uso (requiere Node.js; es solo una herramienta de desarrollo, no una
// dependencia de la aplicacion):
//
//   cd tools
//   npm install @resvg/resvg-js
//   node make_icon.mjs
//
// Salida: ../src/ControlTime.App/Assets/controltime.ico
//
// El icono consumido por la app (csproj ApplicationIcon, Icon de ventana y
// icono de bandeja) es el .ico generado; el SVG es la fuente editable.

import { Resvg } from '@resvg/resvg-js';
import { fileURLToPath } from 'node:url';
import { dirname, join } from 'node:path';
import fs from 'node:fs';

const here = dirname(fileURLToPath(import.meta.url));
const svgPath = join(here, '..', 'src', 'ControlTime.App', 'Assets', 'controltime.svg');
const icoPath = join(here, '..', 'src', 'ControlTime.App', 'Assets', 'controltime.ico');

const sizes = [16, 20, 24, 32, 40, 48, 64, 128, 256];
const svg = fs.readFileSync(svgPath, 'utf8');

const pngs = sizes.map((size) => {
  const resvg = new Resvg(svg, {
    fitTo: { mode: 'width', value: size },
    background: 'rgba(0,0,0,0)',
  });
  return { size, data: resvg.render().asPng() };
});

const count = pngs.length;
let offset = 6 + count * 16;
const entries = [];
for (const png of pngs) {
  const dimension = png.size >= 256 ? 0 : png.size;
  const entry = Buffer.alloc(16);
  entry.writeUInt8(dimension, 0);
  entry.writeUInt8(dimension, 1);
  entry.writeUInt8(0, 2);
  entry.writeUInt8(0, 3);
  entry.writeUInt16LE(1, 4);
  entry.writeUInt16LE(32, 6);
  entry.writeUInt32LE(png.data.length, 8);
  entry.writeUInt32LE(offset, 12);
  entries.push(entry);
  offset += png.data.length;
}

const header = Buffer.alloc(6);
header.writeUInt16LE(0, 0);
header.writeUInt16LE(1, 2);
header.writeUInt16LE(count, 4);

fs.writeFileSync(icoPath, Buffer.concat([header, ...entries, ...pngs.map((p) => p.data)]));
console.log(`ICO generado: ${icoPath} (${count} tamanos, ${fs.statSync(icoPath).size} bytes)`);
