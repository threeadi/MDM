/**
 * Steganography HLS Decrypter for MDM
 * Unpacks concatenated JPEG-wrapped AES-CBC video chunks and PNG-wrapped MPEG-TS chunks
 * and remuxes them to clean, playable MP4.
 */
const fs = require('fs');
const path = require('path');
const os = require('os');
const { execSync } = require('child_process');

function findFfmpeg() {
  try {
    execSync('ffmpeg -version', { stdio: 'ignore' });
    return 'ffmpeg';
  } catch (_) {}

  const localAppData = process.env.LOCALAPPDATA || '';
  const candidates = [
    path.join(__dirname, '..', 'ffmpeg.exe'),
    path.join(__dirname, '..', 'bin', 'ffmpeg.exe'),
    path.join(localAppData, 'Microsoft', 'WinGet', 'Links', 'ffmpeg.exe'),
  ];
  for (const c of candidates) {
    if (fs.existsSync(c)) return c;
  }
  return 'ffmpeg';
}

function rc4(data, key) {
  for (var n, o = [], r = 0, l = '', a = 0; a < 256; a++) o[a] = a;
  for (a = 0; a < 256; a++) r = (r + o[a] + key.charCodeAt(a % key.length)) % 256, n = o[a], o[a] = o[r], o[r] = n;
  a = 0, r = 0;
  for (var i = 0; i < data.length; i++) r = (r + o[a = (a + 1) % 256]) % 256, n = o[a], o[a] = o[r], o[r] = n, l += String.fromCharCode(data.charCodeAt(i) ^ o[(o[a] + o[r]) % 256]);
  return l;
}

const AesEngine = function () {
  function e() {
    this.rcon = [0, 1, 2, 4, 8, 16, 32, 64, 128, 27, 54];
    this.subMix = [new Uint32Array(256), new Uint32Array(256), new Uint32Array(256), new Uint32Array(256)];
    this.invSubMix = [new Uint32Array(256), new Uint32Array(256), new Uint32Array(256), new Uint32Array(256)];
    this.sBox = new Uint32Array(256);
    this.invSBox = new Uint32Array(256);
    this.key = new Uint32Array(0);
    this.ksRows = 0;
    this.keySize = 0;
    this.keySchedule = void 0;
    this.invKeySchedule = void 0;
    this.initTable();
  }
  var t = e.prototype;
  t.uint8ArrayToUint32Array_ = function (e) {
    for (var t = new DataView(e), n = new Uint32Array(4), o = 0; o < 4; o++) n[o] = t.getUint32(4 * o);
    return n;
  };
  t.initTable = function () {
    var e = this.sBox, t = this.invSBox, n = this.subMix, o = n[0], r = n[1], l = n[2], a = n[3], i = this.invSubMix, s = i[0], d = i[1], c = i[2], p = i[3], u = new Uint32Array(256), y = 0, f = 0, m = 0;
    for (m = 0; m < 256; m++) u[m] = m < 128 ? m << 1 : m << 1 ^ 283;
    for (m = 0; m < 256; m++) {
      var h = f ^ f << 1 ^ f << 2 ^ f << 3 ^ f << 4;
      h = h >>> 8 ^ 255 & h ^ 99, e[y] = h, t[h] = y;
      var b = u[y], g = u[b], w = u[g], v = 257 * u[h] ^ 16843008 * h;
      o[y] = v << 24 | v >>> 8, r[y] = v << 16 | v >>> 16, l[y] = v << 8 | v >>> 24, a[y] = v, v = 16843009 * w ^ 65537 * g ^ 257 * b ^ 16843008 * y, s[h] = v << 24 | v >>> 8, d[h] = v << 16 | v >>> 16, c[h] = v << 8 | v >>> 24, p[h] = v, y ? (y = b ^ u[u[u[w ^ b]]], f ^= u[u[f]]) : y = f = 1;
    }
  };
  t.expandKey = function (e) {
    for (var t = this.uint8ArrayToUint32Array_(e), n = !0, o = 0; o < t.length && n;) n = t[o] === this.key[o], o++;
    if (!n) {
      this.key = t;
      var r = this.keySize = t.length;
      if (4 !== r && 6 !== r && 8 !== r) throw new Error('Invalid aes key size=' + r);
      var l, a, i, s, d = this.ksRows = 4 * (r + 6 + 1), c = this.keySchedule = new Uint32Array(d), p = this.invKeySchedule = new Uint32Array(d), u = this.sBox, y = this.rcon, f = this.invSubMix, m = f[0], h = f[1], b = f[2], g = f[3];
      for (l = 0; l < d; l++) l < r ? i = c[l] = t[l] : (s = i, l % r === 0 ? (s = u[(s = s << 8 | s >>> 24) >>> 24] << 24 | u[s >>> 16 & 255] << 16 | u[s >>> 8 & 255] << 8 | u[255 & s], s ^= y[l / r | 0] << 24) : r > 6 && l % r === 4 && (s = u[s >>> 24] << 24 | u[s >>> 16 & 255] << 16 | u[s >>> 8 & 255] << 8 | u[255 & s]), c[l] = i = (c[l - r] ^ s) >>> 0);
      for (a = 0; a < d; a++) l = d - a, s = 3 & a ? c[l] : c[l - 4], p[a] = a < 4 || l <= 4 ? s : m[u[s >>> 24]] ^ h[u[s >>> 16 & 255]] ^ b[u[s >>> 8 & 255]] ^ g[u[255 & s]], p[a] = p[a] >>> 0;
    }
  };
  t.networkToHostOrderSwap = function (e) {
    return e << 24 | (65280 & e) << 8 | (16711680 & e) >> 8 | e >>> 24;
  };
  t.decrypt = function (e, t, n) {
    for (var o, r, l, a, i, s, d, c, p, u, y, f, m, h, b = this.keySize + 6, g = this.invKeySchedule, w = this.invSBox, v = this.invSubMix, k = v[0], E = v[1], S = v[2], x = v[3], I = this.uint8ArrayToUint32Array_(n), T = I[0], _ = I[1], A = I[2], C = I[3], L = new Int32Array(e), U = new Int32Array(L.length), B = this.networkToHostOrderSwap; t < L.length;) {
      for (p = B(L[t]), u = B(L[t + 1]), y = B(L[t + 2]), f = B(L[t + 3]), i = p ^ g[0], s = f ^ g[1], d = y ^ g[2], c = u ^ g[3], m = 4, h = 1; h < b; h++) o = k[i >>> 24] ^ E[s >> 16 & 255] ^ S[d >> 8 & 255] ^ x[255 & c] ^ g[m], r = k[s >>> 24] ^ E[d >> 16 & 255] ^ S[c >> 8 & 255] ^ x[255 & i] ^ g[m + 1], l = k[d >>> 24] ^ E[c >> 16 & 255] ^ S[i >> 8 & 255] ^ x[255 & s] ^ g[m + 2], a = k[c >>> 24] ^ E[i >> 16 & 255] ^ S[s >> 8 & 255] ^ x[255 & d] ^ g[m + 3], i = o, s = r, d = l, c = a, m += 4;
      o = w[i >>> 24] << 24 ^ w[s >> 16 & 255] << 16 ^ w[d >> 8 & 255] << 8 ^ w[255 & c] ^ g[m], r = w[s >>> 24] << 24 ^ w[d >> 16 & 255] << 16 ^ w[c >> 8 & 255] << 8 ^ w[255 & i] ^ g[m + 1], l = w[d >>> 24] << 24 ^ w[c >> 16 & 255] << 16 ^ w[i >> 8 & 255] << 8 ^ w[255 & s] ^ g[m + 2], a = w[c >>> 24] << 24 ^ w[i >> 16 & 255] << 16 ^ w[s >> 8 & 255] << 8 ^ w[255 & d] ^ g[m + 3], U[t] = B(o ^ T), U[t + 1] = B(a ^ _), U[t + 2] = B(l ^ A), U[t + 3] = B(r ^ C), T = p, _ = u, A = y, C = f, t += 4;
    }
    return U.buffer;
  };
  return e;
}();

function parseHex(e) {
  if (e.length % 2 !== 0) throw new Error('Invalid hex string');
  const t = new Uint8Array(e.length / 2);
  for (let n = 0; n < e.length; n += 2) t[n / 2] = parseInt(e.substr(n, 2), 16);
  return t;
}

function decryptChunk(raw, p, ivBytes) {
  let a = new Uint8Array(raw), i = 0, foundSoi = false;
  for (; i < 1000; i++) {
    if (a[i] === 0xFF && a[i + 1] === 0xD8) { foundSoi = true; break; }
  }
  if (!foundSoi) return null;

  for (; i < a.length && !(a[i] === 0xFF && a[i + 1] === 0xD9);) i++;
  if (i >= a.length) return null;

  let l = a.subarray(i + 2, a.length);
  if (l.length < 16) return null;

  let u = new Uint8Array(l.length), y = ivBytes;
  for (let idx = 0; idx < l.length; idx += 16) {
    let n = l.subarray(idx, idx + 16), blk = new Uint8Array(16);
    blk.set(n);
    let dec = p.decrypt(blk.buffer, 0, y.buffer);
    u.set(new Uint8Array(dec), idx);
    y = n.slice(-16);
  }

  // PKCS7 unpad
  const nLen = u.byteLength;
  const pad = nLen && new DataView(u.buffer).getUint8(nLen - 1);
  if (pad && pad <= 16) {
    u = u.slice(0, nLen - pad);
  }
  return Buffer.from(u);
}

function processJpegDecoy(filePath, raw) {
  const matches = [];
  for (let i = 0; i < raw.length - 4; i++) {
    if (raw[i] === 0xFF && raw[i + 1] === 0xD8 && raw[i + 2] === 0xFF && raw[i + 3] === 0xE0) {
      matches.push(i);
    }
  }
  matches.push(raw.length);

  if (matches.length <= 1) {
    console.error('No multiple JPEG segment boundaries found');
    process.exit(1);
  }

  console.log(`Found ${matches.length - 1} JPEG segments. Preparing AES decrypter...`);

  let keyStr = rc4(Buffer.from('7zavSRLyuvK8CqSKj1B511h81JudQwGap+5qNjn1OM4/kLHuQl8EzjYTSaa+88TgRWzS/o3jlLlc89UVc/K/Zw==', 'base64').toString('binary'), 'HTTPS');
  let ivStr = rc4(Buffer.from('52P6QESp76DtW/WJj1d10Astjp2bT1XL87ZuMWunYM0=', 'base64').toString('binary'), 'HTTPS');
  const keyBytes = parseHex(keyStr);
  const ivBytes = parseHex(ivStr);

  const p = new AesEngine();
  p.expandKey(keyBytes.buffer);

  const randSuffix = `${Date.now()}_${Math.random().toString(36).slice(2, 8)}`;
  const tempTs = path.join(os.tmpdir(), `mdm_stegano_${randSuffix}.ts`);
  const tempMp4 = path.join(os.tmpdir(), `mdm_stegano_${randSuffix}.mp4`);
  const fd = fs.openSync(tempTs, 'w');

  let decryptedCount = 0;
  try {
    for (let idx = 0; idx < matches.length - 1; idx++) {
      const seg = raw.subarray(matches[idx], matches[idx + 1]);
      const dec = decryptChunk(seg, p, ivBytes);
      if (dec && dec.length > 0) {
        fs.writeSync(fd, dec);
        decryptedCount++;
      }
    }
  } finally {
    fs.closeSync(fd);
  }

  console.log(`Decrypted ${decryptedCount}/${matches.length - 1} segments to TS.`);
  if (decryptedCount === 0) {
    try { fs.unlinkSync(tempTs); } catch (_) {}
    process.exit(1);
  }

  const ffmpeg = findFfmpeg();
  try {
    console.log('Remuxing TS to clean MP4 via ffmpeg...');
    try {
      execSync(`"${ffmpeg}" -y -v error -i "${tempTs}" -map 0:v? -map 0:a? -c copy "${tempMp4}"`);
    } catch (_) {
      execSync(`"${ffmpeg}" -y -v error -i "${tempTs}" -c copy "${tempMp4}"`);
    }

    if (fs.existsSync(tempMp4) && fs.statSync(tempMp4).size > 1024) {
      fs.copyFileSync(tempMp4, filePath);
      console.log('Successfully decrypted JPEG decoy and replaced file in-place!');
    } else {
      throw new Error('ffmpeg did not produce a valid output file');
    }
  } catch (err) {
    console.error('Failed to remux with ffmpeg:', err.message);
    process.exit(1);
  } finally {
    try { if (fs.existsSync(tempTs)) fs.unlinkSync(tempTs); } catch (_) {}
    try { if (fs.existsSync(tempMp4)) fs.unlinkSync(tempMp4); } catch (_) {}
  }
}

function processPngDecoy(filePath, raw) {
  const pngSig = Buffer.from([0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A]);
  const matches = [];
  let idx = 0;
  while (idx < raw.length) {
    const pos = raw.indexOf(pngSig, idx);
    if (pos === -1) break;
    matches.push(pos);
    idx = pos + 8;
  }
  matches.push(raw.length);

  const totalSegments = matches.length - 1;
  console.log(`Found ${totalSegments} PNG-prefixed segment(s). Inspecting payloads...`);

  const randSuffix = `${Date.now()}_${Math.random().toString(36).slice(2, 8)}`;
  const tempTs = path.join(os.tmpdir(), `mdm_stegano_png_${randSuffix}.ts`);
  const tempMp4 = path.join(os.tmpdir(), `mdm_stegano_png_${randSuffix}.mp4`);
  const fd = fs.openSync(tempTs, 'w');

  let extractedTsCount = 0;
  const iendSig = Buffer.from('IEND');

  try {
    for (let i = 0; i < totalSegments; i++) {
      const seg = raw.subarray(matches[i], matches[i + 1]);
      const iendPos = seg.indexOf(iendSig);
      let foundTs = -1;
      const startSearch = iendPos !== -1 ? iendPos + 8 : 8;

      // Look for MPEG-TS sync byte 0x47 with 188-byte stride
      for (let o = startSearch; o < Math.min(seg.length, 4096); o++) {
        if (seg[o] === 0x47) {
          if (o + 188 < seg.length && seg[o + 188] === 0x47) {
            foundTs = o;
            break;
          }
        }
      }

      if (foundTs !== -1) {
        fs.writeSync(fd, seg.subarray(foundTs));
        extractedTsCount++;
      }
    }
  } finally {
    fs.closeSync(fd);
  }

  console.log(`Extracted TS payload from ${extractedTsCount}/${totalSegments} segments.`);

  if (extractedTsCount === 0) {
    try { fs.unlinkSync(tempTs); } catch (_) {}
    console.error('No valid MPEG-TS stream found behind PNG decoy headers');
    process.exit(1);
  }

  const ffmpeg = findFfmpeg();
  try {
    console.log('Remuxing extracted TS to clean MP4 via ffmpeg...');
    try {
      execSync(`"${ffmpeg}" -y -v error -i "${tempTs}" -map 0:v? -map 0:a? -c copy "${tempMp4}"`);
    } catch (_) {
      execSync(`"${ffmpeg}" -y -v error -i "${tempTs}" -c copy "${tempMp4}"`);
    }

    if (fs.existsSync(tempMp4) && fs.statSync(tempMp4).size > 1024) {
      fs.copyFileSync(tempMp4, filePath);
      console.log('Successfully unpacked PNG decoy and remuxed to MP4 in-place!');
    } else {
      throw new Error('ffmpeg did not produce a valid output file');
    }
  } catch (err) {
    console.error('Failed to remux with ffmpeg:', err.message);
    process.exit(1);
  } finally {
    try { if (fs.existsSync(tempTs)) fs.unlinkSync(tempTs); } catch (_) {}
    try { if (fs.existsSync(tempMp4)) fs.unlinkSync(tempMp4); } catch (_) {}
  }
}

function processFile(filePath) {
  if (!fs.existsSync(filePath)) {
    console.error(`File not found: ${filePath}`);
    process.exit(1);
  }

  const raw = fs.readFileSync(filePath);
  if (raw.length < 1024) {
    console.error('File too small');
    process.exit(1);
  }

  const isJpeg = raw[0] === 0xFF && raw[1] === 0xD8 && raw[2] === 0xFF;
  const isPng = raw[0] === 0x89 && raw[1] === 0x50 && raw[2] === 0x4E && raw[3] === 0x47 &&
                raw[4] === 0x0D && raw[5] === 0x0A && raw[6] === 0x1A && raw[7] === 0x0A;

  if (isJpeg) {
    processJpegDecoy(filePath, raw);
  } else if (isPng) {
    processPngDecoy(filePath, raw);
  } else {
    console.log('Not a recognized steganography decoy stream (JPEG/PNG).');
    process.exit(0);
  }
}

const targetPath = process.argv[2];
if (!targetPath) {
  console.log('Usage: node stegano_decrypt.js <filePath>');
  process.exit(1);
}
processFile(targetPath);
