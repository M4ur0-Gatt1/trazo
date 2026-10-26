// EX-01.8 — verificación cruzada del PSD de Trazo con ag-psd, una implementación independiente.
//   node interop.mjs read <archivo.psd>   → imprime un resumen JSON de lo que ag-psd lee
//   node interop.mjs write <archivo.psd>  → escribe con ag-psd el documento de prueba que lee Trazo
// La prueba EX01_PsdInteropTests (Trazo.Core.Tests) llama a este script y compara.
import { readFileSync, writeFileSync } from 'node:fs';
import { createHash } from 'node:crypto';
import { initializeCanvas, readPsd, writePsd } from 'ag-psd';

// sin navegador no hay canvas: solo hacen falta datos de imagen en memoria
initializeCanvas(
    () => { throw new Error('canvas no disponible'); },
    (width, height) => ({ width, height, data: new Uint8ClampedArray(width * height * 4) }),
);

const hash = (bytes) => createHash('sha256').update(bytes).digest('hex');

/** Un canal (0 = R…3 = A) de unos datos RGBA. */
function channel(data, c) {
    const o = new Uint8Array(data.length / 4);
    for (let i = 0; i < o.length; i++) o[i] = data[i * 4 + c];
    return o;
}

function summarize(layer) {
    const s = {
        name: layer.name,
        opacity: Math.round((layer.opacity ?? 1) * 255),
        hidden: !!layer.hidden,
        blendMode: layer.blendMode ?? 'normal',
        clipping: !!layer.clipping,
    };
    if (layer.children) {
        s.opened = !!layer.opened;
        s.children = layer.children.map(summarize);
        return s;
    }
    s.rect = [layer.left ?? 0, layer.top ?? 0, layer.right ?? 0, layer.bottom ?? 0];
    s.rgba = layer.imageData ? hash(layer.imageData.data) : null;
    if (layer.mask) {
        const m = layer.mask;
        s.mask = {
            rect: [m.left ?? 0, m.top ?? 0, m.right ?? 0, m.bottom ?? 0],
            defaultColor: m.defaultColor ?? 0,
            gray: m.imageData ? hash(channel(m.imageData.data, 0)) : null,
        };
    }
    return s;
}

/** RGBA de w×h a partir de una función (x, y) → [r, g, b, a]; la misma fórmula está en la prueba de C#. */
function pixels(w, h, f) {
    const data = new Uint8ClampedArray(w * h * 4);
    for (let y = 0; y < h; y++)
        for (let x = 0; x < w; x++) data.set(f(x, y), (y * w + x) * 4);
    return { width: w, height: h, data };
}

const [mode, file] = process.argv.slice(2);
if (mode === 'read') {
    const psd = readPsd(readFileSync(file), { useImageData: true, skipThumbnail: true });
    const out = {
        width: psd.width,
        height: psd.height,
        composite: psd.imageData ? hash(psd.imageData.data) : null,
        children: (psd.children ?? []).map(summarize),
    };
    process.stdout.write(JSON.stringify(out, null, 2));
} else if (mode === 'write') {
    const W = 40, H = 30;
    const psd = {
        width: W,
        height: H,
        imageData: pixels(W, H, (x, y) => [x * 6, y * 8, 90, 255]),
        children: [
            { name: 'Fondo', left: 0, top: 0, imageData: pixels(W, H, () => [250, 245, 235, 255]) },
            {
                name: 'Personaje', opened: false, opacity: 128 / 255, blendMode: 'screen',
                children: [
                    {
                        name: 'Línea — ñandú', left: 3, top: 4, opacity: 200 / 255, blendMode: 'multiply',
                        imageData: pixels(20, 10, (x, y) => [x * 12, y * 25, 7, x * 13]),
                        mask: {
                            left: 0, top: 0, right: W, bottom: H, defaultColor: 255,
                            imageData: pixels(W, H, (x, y) => { const v = (y * W + x) % 256; return [v, v, v, 255]; }),
                        },
                    },
                    {
                        name: 'Sombra', left: 10, top: 10, hidden: true, clipping: true, blendMode: 'luminosity',
                        imageData: pixels(2, 1, (x) => [1 + 4 * x, 2 + 4 * x, 3 + 4 * x, 4 + 4 * x]),
                    },
                ],
            },
            { name: 'Luz lineal', left: 5, top: 5, blendMode: 'linear light', imageData: pixels(3, 3, () => [200, 100, 50, 255]) },
        ],
    };
    writeFileSync(file, Buffer.from(writePsd(psd, { generateThumbnail: false })));
} else {
    console.error('uso: node interop.mjs read|write <archivo.psd>');
    process.exit(2);
}
