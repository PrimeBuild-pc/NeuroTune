import type { SupportingAttachment } from './types';
import { t } from './i18n';

export const supportLimits = { files: 8, images: 4, reportBytes: 512 * 1024, reportCharacters: 40_000, combinedReportCharacters: 80_000, sourceImageBytes: 20 * 1024 * 1024, imageBytes: 768 * 1024, imageSide: 1600 };
export const supportAccept = '.txt,.log,.csv,.json,.xml,.html,.htm,.png,.jpg,.jpeg';
export function validateSupportList(files: SupportingAttachment[]) {
  if (files.length > supportLimits.files || files.filter(item => item.kind === 'image').length > supportLimits.images) throw new Error(t('Maximum 8 files, including 4 screenshots.'));
  if (files.filter(item => item.kind === 'report').reduce((sum, item) => sum + item.content.length, 0) > supportLimits.combinedReportCharacters) throw new Error(t('Combined reports exceed 80,000 characters; select smaller excerpts.'));
}
function invalidControl(text: string, allowWhitespace = false): boolean {
  for (const character of text) {
    const code = character.charCodeAt(0);
    if ((code < 32 || code >= 127 && code <= 159) && !(allowWhitespace && '\r\n\t'.includes(character))) return true;
  }
  return false;
}
export function decodeReport(bytes: Uint8Array): string {
  if (bytes.length > supportLimits.reportBytes) throw new Error(t('Report source exceeds 512 KiB. Export a smaller report or excerpt.'));
  const encoding = bytes[0] === 255 && bytes[1] === 254 ? 'utf-16le' : bytes[0] === 254 && bytes[1] === 255 ? 'utf-16be' : 'utf-8';
  const text = new TextDecoder(encoding, { fatal: true }).decode(bytes);
  if (!text.trim() || text.length > supportLimits.reportCharacters || invalidControl(text, true)) throw new Error(t('Report must contain readable UTF-8/UTF-16 text, at most 40,000 characters. Binary/active documents are not supported.'));
  return text;
}
export function imageDimensions(bytes: Uint8Array): { width: number; height: number; type: string } {
  const view = new DataView(bytes.buffer, bytes.byteOffset, bytes.byteLength);
  let width = 0, height = 0, type = '';
  if (bytes.length >= 24 && [137, 80, 78, 71, 13, 10, 26, 10].every((value, index) => bytes[index] === value) && String.fromCharCode(...bytes.slice(12, 16)) === 'IHDR') {
    width = view.getUint32(16); height = view.getUint32(20); type = 'image/png';
    for (let offset = 8; offset <= bytes.length - 12;) {
      const length = view.getUint32(offset);
      if (length > bytes.length - offset - 12) throw new Error(t('Malformed source PNG chunk.'));
      if (['acTL', 'fcTL', 'fdAT'].includes(String.fromCharCode(...bytes.slice(offset + 4, offset + 8)))) throw new Error(t('Use a static screenshot, not an animated PNG.'));
      offset += length + 12;
    }
  } else if (bytes[0] === 255 && bytes[1] === 216) {
    let offset = 2;
    while (offset < bytes.length - 4) {
      if (bytes[offset++] !== 255) throw new Error(t('Malformed JPEG marker.'));
      while (bytes[offset] === 255) offset++;
      const marker = bytes[offset++];
      if (marker === 217 || marker === 218) break;
      if (marker === 1 || marker >= 208 && marker <= 215) continue;
      if (offset > bytes.length - 2) break;
      const length = view.getUint16(offset);
      if (length < 2 || offset + length > bytes.length) throw new Error(t('Malformed JPEG segment.'));
      if ([192, 193, 194].includes(marker)) {
        if (length < 8) throw new Error(t('Invalid JPEG frame.'));
        height = view.getUint16(offset + 3); width = view.getUint16(offset + 5); type = 'image/jpeg'; break;
      }
      offset += length;
    }
  }
  if (!width || !height || width > 8192 || height > 8192 || width * height > 20_000_000) throw new Error(t('Use a PNG/JPEG screenshot up to 8192 pixels per side and 20 megapixels. Animated/vector files are not accepted.'));
  return { width, height, type };
}
export async function prepareSupportingFile(file: File): Promise<SupportingAttachment> {
  const extension = file.name.split('.').pop()?.toLowerCase();
  const image = ['png', 'jpg', 'jpeg'].includes(extension ?? '');
  if (!image && !['txt', 'log', 'csv', 'json', 'xml', 'html', 'htm'].includes(extension ?? '')) throw new Error(t('Use TXT/LOG/CSV/JSON/XML/HTML reports or PNG/JPEG screenshots. PDF, executables and archives are not supported.'));
  if (file.name.length > 120 || (/[\\/:]/.test(file.name) || invalidControl(file.name))) throw new Error(t('Use a short filename without path characters.'));
  if (file.size > (image ? supportLimits.sourceImageBytes : supportLimits.reportBytes)) throw new Error(image ? t('Screenshot source exceeds 20 MiB.') : t('Report source exceeds 512 KiB.'));
  const bytes = new Uint8Array(await file.arrayBuffer());
  if (!image) return { id: crypto.randomUUID(), name: file.name, kind: 'report', contentType: 'text/plain', content: decodeReport(bytes), sha256: '' };
  const dimensions = imageDimensions(bytes);
  const bitmap = await createImageBitmap(new Blob([bytes], { type: dimensions.type }));
  try {
    const canvas = document.createElement('canvas'); const context = canvas.getContext('2d');
    if (!context) throw new Error(t('Screenshot preparation is unavailable in this WebView.'));
    let side = Math.min(supportLimits.imageSide, Math.max(bitmap.width, bitmap.height));
    for (let attempt = 0; attempt < 6; attempt++) {
      const scale = Math.min(1, side / Math.max(bitmap.width, bitmap.height));
      canvas.width = Math.max(1, Math.round(bitmap.width * scale)); canvas.height = Math.max(1, Math.round(bitmap.height * scale));
      context.drawImage(bitmap, 0, 0, canvas.width, canvas.height);
      const blob = await new Promise<Blob>((resolve, reject) => canvas.toBlob(value => value ? resolve(value) : reject(new Error(t('PNG preparation failed.'))), 'image/png'));
      if (blob.size <= supportLimits.imageBytes) {
        const encoded = new Uint8Array(await blob.arrayBuffer());
        let binary = ''; for (let offset = 0; offset < encoded.length; offset += 8192) binary += String.fromCharCode(...encoded.slice(offset, offset + 8192));
        return { id: crypto.randomUUID(), name: file.name, kind: 'image', contentType: 'image/png', content: btoa(binary), sha256: '' };
      }
      side = Math.floor(side * .8);
    }
    throw new Error(t('Screenshot remains too large. Crop it before uploading; nothing was sent to AI.'));
  } finally { bitmap.close(); }
}
