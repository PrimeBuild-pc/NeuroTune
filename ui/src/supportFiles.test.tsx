import { renderToStaticMarkup } from 'react-dom/server';
import { describe, expect, it } from 'vitest';
import { SupportingFiles } from './SupportingFiles';
import { decodeReport, imageDimensions, prepareSupportingFile, validateSupportList } from './supportFiles';
import type { SupportingAttachment } from './types';

const report: SupportingAttachment = { id: 'report', name: 'cpu-z.txt', kind: 'report', contentType: 'text/plain', content: '<script>do not execute</script>', sha256: 'prepared-hash' };
describe('optional supporting files', () => {
  it('decodes UTF-8/UTF-16 exports but rejects binary, malformed encodings and oversized reports without slicing', async () => {
    expect(decodeReport(new TextEncoder().encode('CPU clock: 4200 MHz'))).toBe('CPU clock: 4200 MHz');
    expect(decodeReport(new Uint8Array([255, 254, 65, 0, 10, 0]))).toBe('A\n');
    expect(() => decodeReport(new Uint8Array([0, 1, 2]))).toThrow('readable');
    expect(() => decodeReport(new Uint8Array([192, 128]))).toThrow();
    expect(() => decodeReport(new TextEncoder().encode('x'.repeat(40001)))).toThrow('40,000');
    await expect(prepareSupportingFile(new File(['code'], 'tool.exe'))).rejects.toThrow('not supported');
    const text = await prepareSupportingFile(new File(['CPU: AMD'], 'report.txt'));
    expect(text.content).toBe('CPU: AMD'); expect(text.kind).toBe('report');
  });
  it('bounds count and total text, and detects oversized images before decoding them', () => {
    expect(() => validateSupportList(Array.from({ length: 9 }, () => report))).toThrow('8');
    expect(() => validateSupportList(Array.from({ length: 5 }, () => ({ ...report, kind: 'image' as const })))).toThrow('4');
    expect(() => validateSupportList(Array.from({ length: 3 }, () => ({ ...report, content: 'x'.repeat(40000) })))).toThrow('80,000');
    const png = new Uint8Array(24); png.set([137, 80, 78, 71, 13, 10, 26, 10]); png.set(new TextEncoder().encode('IHDR'), 12);
    const view = new DataView(png.buffer); view.setUint32(16, 1920); view.setUint32(20, 1080);
    expect(imageDimensions(png)).toEqual({ width: 1920, height: 1080, type: 'image/png' });
    view.setUint32(16, 99999); expect(() => imageDimensions(png)).toThrow('20 megapixels');
    expect(() => imageDimensions(new TextEncoder().encode('<svg/>'))).toThrow();
  });
  it('shows inert editable report text and separate preview/privacy/vision consent, never embedding report HTML', () => {
    const html = renderToStaticMarkup(<SupportingFiles files={[report, { ...report, id: 'image', name: 'gpu-z.png', kind: 'image', contentType: 'image/png', content: 'fake-for-static-markup' }]} onFiles={() => {}} onReady={() => {}} onVision={() => {}} disabled={false}/>);
    expect(html).toContain('&lt;script&gt;do not execute&lt;/script&gt;');
    expect(html).not.toContain('<script>do not execute</script>');
    expect(html).toContain('Il modello selezionato supporta immagini');
    expect(html).toContain('NON sono anonimizzate'); expect(html).toContain('non vengono salvati');
    expect(html).not.toContain('checked=""'); expect(html).toContain('Rimuovi gpu-z.png');
  });
});
