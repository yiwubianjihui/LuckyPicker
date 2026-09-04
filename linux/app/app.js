'use strict';
// ================================================================
// 幸运摇人器 · 跨平台 Web 核心（Android WebView / Linux Electron / 浏览器通用）
// 无任何外部依赖（pako 用于 .xlsx 解压）。逻辑与 TTS 均可在 Node 中测试。
// ================================================================
(function () {
  var DEFAULT_DATA = {"classes":{"1":"示例一班","2":"示例二班","3":"示例三班"},"students":[{"name":"张伟","classId":"1","gender":"男"},{"name":"王芳","classId":"1","gender":"女"},{"name":"李娜","classId":"1","gender":"女"},{"name":"刘洋","classId":"1","gender":"男"},{"name":"陈静","classId":"1","gender":"女"},{"name":"杨帆","classId":"1","gender":"男"},{"name":"赵磊","classId":"1","gender":"男"},{"name":"黄敏","classId":"1","gender":"女"},{"name":"周杰","classId":"1","gender":"男"},{"name":"吴迪","classId":"1","gender":"男"},{"name":"徐婷","classId":"1","gender":"女"},{"name":"孙强","classId":"1","gender":"男"},{"name":"马超","classId":"1","gender":"男"},{"name":"朱琳","classId":"1","gender":"女"},{"name":"胡军","classId":"1","gender":"男"},{"name":"林晓","classId":"1","gender":"女"},{"name":"郭涛","classId":"1","gender":"男"},{"name":"何佳","classId":"1","gender":"女"},{"name":"高翔","classId":"2","gender":"男"},{"name":"罗雪","classId":"2","gender":"女"},{"name":"郑帅","classId":"2","gender":"男"},{"name":"梁月","classId":"2","gender":"女"},{"name":"谢东","classId":"2","gender":"男"},{"name":"宋佳","classId":"2","gender":"女"},{"name":"唐磊","classId":"2","gender":"男"},{"name":"许诺","classId":"2","gender":"女"},{"name":"韩冰","classId":"2","gender":"男"},{"name":"冯远","classId":"2","gender":"男"},{"name":"邓琪","classId":"2","gender":"女"},{"name":"曹阳","classId":"2","gender":"男"},{"name":"彭飞","classId":"2","gender":"男"},{"name":"曾露","classId":"2","gender":"女"},{"name":"潘阳","classId":"2","gender":"男"},{"name":"袁圆","classId":"2","gender":"女"},{"name":"蔡明","classId":"2","gender":"男"},{"name":"蒋一","classId":"2","gender":"男"},{"name":"余晖","classId":"3","gender":"男"},{"name":"杜鹃","classId":"3","gender":"女"},{"name":"叶舟","classId":"3","gender":"男"},{"name":"程琳","classId":"3","gender":"女"},{"name":"苏帆","classId":"3","gender":"男"},{"name":"魏东","classId":"3","gender":"男"},{"name":"丁兰","classId":"3","gender":"女"},{"name":"任平","classId":"3","gender":"男"},{"name":"沈虹","classId":"3","gender":"女"},{"name":"姚远","classId":"3","gender":"男"},{"name":"卢光","classId":"3","gender":"男"},{"name":"傅颖","classId":"3","gender":"女"},{"name":"钟意","classId":"3","gender":"男"},{"name":"姜潮","classId":"3","gender":"男"}]};

  // ---------- 纯 JS SHA-256 / HMAC（避免 file:// 下 WebCrypto 不可用） ----------
  var Sha256 = (function () {
    var K = [0x428a2f98,0x71374491,0xb5c0fbcf,0xe9b5dba5,0x3956c25b,0x59f111f1,0x923f82a4,0xab1c5ed5,
      0xd807aa98,0x12835b01,0x243185be,0x550c7dc3,0x72be5d74,0x80deb1fe,0x9bdc06a7,0xc19bf174,
      0xe49b69c1,0xefbe4786,0x0fc19dc6,0x240ca1cc,0x2de92c6f,0x4a7484aa,0x5cb0a9dc,0x76f988da,
      0x983e5152,0xa831c66d,0xb00327c8,0xbf597fc7,0xc6e00bf3,0xd5a79147,0x06ca6351,0x14292967,
      0x27b70a85,0x2e1b2138,0x4d2c6dfc,0x53380d13,0x650a7354,0x766a0abb,0x81c2c92e,0x92722c85,
      0xa2bfe8a1,0xa81a664b,0xc24b8b70,0xc76c51a3,0xd192e819,0xd6990624,0xf40e3585,0x106aa070,
      0x19a4c116,0x1e376c08,0x2748774c,0x34b0bcb5,0x391c0cb3,0x4ed8aa4a,0x5b9cca4f,0x682e6ff3,
      0x748f82ee,0x78a5636f,0x84c87814,0x8cc70208,0x90befffa,0xa4506ceb,0xbef9a3f7,0xc67178f2];
    function rotr(x, n) { return (x >>> n) | (x << (32 - n)); }
    function toBytes(msg) {
      var bytes = [];
      for (var i = 0; i < msg.length; i++) {
        var c = msg.charCodeAt(i);
        if (c < 0x80) bytes.push(c);
        else if (c < 0x800) { bytes.push(0xc0 | (c >> 6), 0x80 | (c & 0x3f)); }
        else if (c < 0xd800 || c >= 0xe000) { bytes.push(0xe0 | (c >> 12), 0x80 | ((c >> 6) & 0x3f), 0x80 | (c & 0x3f)); }
        else {
          i++;
          var c2 = msg.charCodeAt(i);
          var cp = 0x10000 + (((c & 0x3ff) << 10) | (c2 & 0x3ff));
          bytes.push(0xf0 | (cp >> 18), 0x80 | ((cp >> 12) & 0x3f), 0x80 | ((cp >> 6) & 0x3f), 0x80 | (cp & 0x3f));
        }
      }
      return bytes;
    }
    function hash(bytes) {
      var h0=0x6a09e667,h1=0xbb67ae85,h2=0x3c6ef372,h3=0xa54ff53a,h4=0x510e527f,h5=0x9b05688c,h6=0x1f83d9ab,h7=0x5be0cd19;
      var l = bytes.length;
      var bitLenHi = Math.floor(l / 0x20000000), bitLenLo = (l << 3) >>> 0;
      var wl = bytes.slice(0);
      wl.push(0x80);
      while (wl.length % 64 !== 56) wl.push(0);
      wl.push((bitLenHi >>> 24) & 0xff,(bitLenHi >>> 16) & 0xff,(bitLenHi >>> 8) & 0xff,bitLenHi & 0xff);
      wl.push((bitLenLo >>> 24) & 0xff,(bitLenLo >>> 16) & 0xff,(bitLenLo >>> 8) & 0xff,bitLenLo & 0xff);
      var w = new Array(64);
      for (var i = 0; i < wl.length; i += 64) {
        for (var t = 0; t < 16; t++) {
          var o = i + t * 4;
          w[t] = ((wl[o] << 24) | (wl[o+1] << 16) | (wl[o+2] << 8) | wl[o+3]) >>> 0;
        }
        for (var t2 = 16; t2 < 64; t2++) {
          var s0 = rotr(w[t2-15],7) ^ rotr(w[t2-15],18) ^ (w[t2-15] >>> 3);
          var s1 = rotr(w[t2-2],17) ^ rotr(w[t2-2],19) ^ (w[t2-2] >>> 10);
          w[t2] = (w[t2-16] + s0 + w[t2-7] + s1) >>> 0;
        }
        var a=h0,b=h1,c=h2,d=h3,e=h4,f=h5,g=h6,h=h7;
        for (var t3 = 0; t3 < 64; t3++) {
          var S1 = rotr(e,6) ^ rotr(e,11) ^ rotr(e,25);
          var ch = (e & f) ^ ((~e) & g);
          var temp1 = (h + S1 + ch + K[t3] + w[t3]) >>> 0;
          var S0 = rotr(a,2) ^ rotr(a,13) ^ rotr(a,22);
          var maj = (a & b) ^ (a & c) ^ (b & c);
          var temp2 = (S0 + maj) >>> 0;
          h=g; g=f; f=e; e=(d+temp1)>>>0; d=c; c=b; b=a; a=(temp1+temp2)>>>0;
        }
        h0=(h0+a)>>>0; h1=(h1+b)>>>0; h2=(h2+c)>>>0; h3=(h3+d)>>>0;
        h4=(h4+e)>>>0; h5=(h5+f)>>>0; h6=(h6+g)>>>0; h7=(h7+h)>>>0;
      }
      function be(n) { return [(n>>>24)&0xff,(n>>>16)&0xff,(n>>>8)&0xff,n&0xff]; }
      return be(h0).concat(be(h1),be(h2),be(h3),be(h4),be(h5),be(h6),be(h7));
    }
    function hmac(keyBytes, msg) {
      var block = 64;
      var k = keyBytes.slice(0);
      if (k.length > block) k = hash(k);
      while (k.length < block) k.push(0);
      var ipad = [], opad = [];
      for (var i = 0; i < block; i++) { ipad.push(k[i] ^ 0x36); opad.push(k[i] ^ 0x5c); }
      return hash(opad.concat(hash(ipad.concat(toBytes(msg)))));
    }
    return { hash: hash, hmac: hmac, toBytes: toBytes };
  })();

  // ---------- 核心逻辑（可测试） ----------
  var Core = {
    normalizeClass: function (s) {
      if (!s) return '';
      s = String(s).trim();
      if (!s.length) return '';
      var digits = '';
      for (var i = 0; i < s.length; i++) {
        var c = s.charAt(i);
        if (c >= '0' && c <= '9') digits += c;
      }
      return digits.length ? digits : s;
    },
    normalizeGender: function (s) {
      if (!s) return '';
      if (s.indexOf('男') >= 0) return '男';
      if (s.indexOf('女') >= 0) return '女';
      return '';
    },
    findColumn: function (rows, keys) {
      if (!rows || !rows.length) return -1;
      var first = rows[0];
      for (var c = 0; c < first.length; c++) {
        var v = String(first[c] || '').trim();
        for (var k = 0; k < keys.length; k++) if (v.length && v.indexOf(keys[k]) >= 0) return c;
      }
      return -1;
    },
    parseCsv: function (text) {
      var rows = [], row = [], sb = '', inQ = false;
      var CR = 13, LF = 10;
      for (var i = 0; i < text.length; i++) {
        var code = text.charCodeAt(i);
        var ch = text.charAt(i);
        if (inQ) {
          if (ch === '"') {
            if (i + 1 < text.length && text.charAt(i + 1) === '"') { sb += '"'; i++; continue; }
            inQ = false;
            continue;
          }
          sb += ch;
          continue;
        }
        if (ch === '"') { inQ = true; continue; }
        if (ch === ',') { row.push(sb); sb = ''; continue; }
        if (code === CR || code === LF) {
          if (code === CR && i + 1 < text.length && text.charCodeAt(i + 1) === LF) i++;
          row.push(sb); sb = '';
          var has = false;
          for (var r2 = 0; r2 < row.length; r2++) if (row[r2].length) { has = true; break; }
          if (has) rows.push(row);
          row = [];
          continue;
        }
        sb += ch;
      }
      if (sb.length || row.length) { row.push(sb); rows.push(row); }
      return rows;
    },
    // 从 .xlsx 字节解析第一个工作表（依赖 pako 解压 deflate）
    parseXlsx: function (u8, pako) {
      if (!pako) return null;
      var entries = {};
      var i = 0;
      while (i + 30 <= u8.length) {
        if (!(u8[i] === 0x50 && u8[i+1] === 0x4B && u8[i+2] === 0x03 && u8[i+3] === 0x04)) break;
        var method = (u8[i+8] | (u8[i+9] << 8)) >>> 0;
        var csize = (u8[i+18] | (u8[i+19] << 8) | (u8[i+20] << 16) | (u8[i+21] << 24)) >>> 0;
        var nameLen = (u8[i+26] | (u8[i+27] << 8)) >>> 0;
        var extraLen = (u8[i+28] | (u8[i+29] << 8)) >>> 0;
        var name = '';
        for (var k = 0; k < nameLen; k++) name += String.fromCharCode(u8[i + 30 + k]);
        var start = i + 30 + nameLen + extraLen;
        var comp = u8.subarray(start, start + csize);
        var data;
        if (method === 0) data = comp;
        else if (method === 8) {
          try { data = pako.inflateRaw(comp); } catch (e) { data = null; }
        } else data = null;
        if (data) entries[name] = data;
        i = start + csize;
      }
      function toStr(a) { try { return new TextDecoder('utf-8').decode(a); } catch (e) { return ''; } }
      function decodeEnt(s) {
        return s.replace(/&amp;/g, '&').replace(/&lt;/g, '<').replace(/&gt;/g, '>')
          .replace(/&quot;/g, '"').replace(/&apos;/g, "'").replace(/&#39;/g, "'").replace(/&#10;/g, ' ');
      }
      function attr(s, name) {
        var re = new RegExp(name + '="([^"]*)"');
        var m = re.exec(s);
        return m ? m[1] : null;
      }
      function extract(s, open, close) {
        var out = [], i2 = 0;
        while (i2 < s.length) {
          var a = s.indexOf(open, i2);
          if (a < 0) break;
          var gt = s.indexOf('>', a);
          if (gt < 0) break;
          var e = s.indexOf(close, gt + 1);
          if (e < 0) break;
          out.push(s.substring(gt + 1, e));
          i2 = e + close.length;
        }
        return out;
      }
      var shared = [];
      var ss = entries['xl/sharedStrings.xml'];
      if (ss) {
        var ssXml = toStr(ss);
        var sis = extract(ssXml, '<si', '</si>');
        for (var si = 0; si < sis.length; si++) {
          var ts = extract(sis[si], '<t', '</t>');
          var sb2 = '';
          for (var ti = 0; ti < ts.length; ti++) sb2 += ts[ti];
          shared.push(decodeEnt(sb2));
        }
      }
      var wbXml = toStr(entries['xl/workbook.xml'] || '');
      var sheetM = /<sheet[^>]*r:id="([^"]+)"/.exec(wbXml);
      if (!sheetM) return null;
      var rid = sheetM[1];
      var relsXml = toStr(entries['xl/_rels/workbook.xml.rels'] || '');
      var target = null;
      var rels = extract(relsXml, '<Relationship', '/>');
      for (var r3 = 0; r3 < rels.length; r3++) {
        if (attr(rels[r3], 'Id') === rid) { target = attr(rels[r3], 'Target'); break; }
      }
      if (!target) return null;
      if (target.charAt(0) === '/') target = target.substring(1); else target = 'xl/' + target;
      var shXml = toStr(entries[target] || '');
      if (!shXml) return null;
      function colIndex(ref) {
        var col = 0, done = false;
        for (var ci = 0; ci < ref.length; ci++) {
          var cc = ref.charCodeAt(ci);
          if (cc >= 65 && cc <= 90) { col = col * 26 + (cc - 64); done = true; }
          else if (cc >= 97 && cc <= 122) { col = col * 26 + (cc - 96); done = true; }
          else break;
        }
        return done ? col - 1 : -1;
      }
      var rows = [];
      var rowParts = extract(shXml, '<row', '</row>');
      for (var rp = 0; rp < rowParts.length; rp++) {
        var cells = [];
        var cStart = 0;
        while (true) {
          var ci2 = rowParts[rp].indexOf('<c ', cStart);
          if (ci2 < 0) break;
          var cEnd = rowParts[rp].indexOf('</c>', ci2);
          var selfClose = rowParts[rp].indexOf('/>', ci2);
          if (selfClose >= 0 && (cEnd < 0 || selfClose < cEnd)) {
            // 自闭合空单元格
            var seg = rowParts[rp].substring(ci2, selfClose + 2);
            var refM = /r="([A-Za-z]+)[0-9]+"/.exec(seg);
            if (refM) {
              var idx0 = colIndex(refM[1]);
              if (idx0 >= 0) { while (cells.length <= idx0) cells.push(''); cells[idx0] = ''; }
            }
            cStart = selfClose + 2;
            continue;
          }
          if (cEnd < 0) break;
          var seg2 = rowParts[rp].substring(ci2, cEnd + 4);
          var refM2 = /r="([A-Za-z]+)[0-9]+"/.exec(seg2);
          var typeM = /t="([^"]+)"/.exec(seg2);
          var type = typeM ? typeM[1] : '';
          var val = '';
          if (type === 's') {
            var vM = /<v>([^]*)<\/v>/.exec(seg2);
            if (vM) { var si2 = parseInt(vM[1], 10); if (!isNaN(si2) && shared[si2] !== undefined) val = shared[si2]; }
          } else if (type === 'inlineStr') {
            var ts2 = extract(seg2, '<t', '</t>');
            for (var ti2 = 0; ti2 < ts2.length; ti2++) val += ts2[ti2];
            val = decodeEnt(val);
          } else {
            var vM2 = /<v>([^]*)<\/v>/.exec(seg2);
            if (vM2) val = decodeEnt(vM2[1]);
          }
          if (refM2) {
            var idx = colIndex(refM2[1]);
            if (idx >= 0) { while (cells.length <= idx) cells.push(''); cells[idx] = val; }
          }
          cStart = cEnd + 4;
        }
        var hasAny = false;
        for (var rc = 0; rc < cells.length; rc++) if (cells[rc] && cells[rc].length) { hasAny = true; break; }
        if (hasAny) rows.push(cells);
      }
      return rows;
    },
    buildStudents: function (rows, mapping) {
      var out = [];
      var start = mapping.header ? 1 : 0;
      for (var i = start; i < rows.length; i++) {
        var name = mapping.name >= 0 ? String(rows[i][mapping.name] || '').trim() : '';
        if (!name.length) continue;
        out.push({
          name: name,
          classId: this.normalizeClass(mapping.cls >= 0 ? rows[i][mapping.cls] : ''),
          gender: this.normalizeGender(mapping.gender >= 0 ? rows[i][mapping.gender] : '')
        });
      }
      return out;
    },
    pickOne: function (pool) {
      if (!pool || !pool.length) return null;
      return pool[Math.floor(Math.random() * pool.length)];
    },
    pickMany: function (pool, n) {
      var copy = pool.slice(0);
      var out = [];
      var limit = Math.min(n, copy.length);
      for (var i = 0; i < limit; i++) {
        var idx = Math.floor(Math.random() * copy.length);
        out.push(copy[idx]);
        copy.splice(idx, 1);
      }
      return out;
    }
  };

  // ---------- TTS 引擎（微软神经语音直连 + 百度 + 本地语音） ----------
  var TTS = {
    voice: 'zh-CN-XiaoxiaoNeural',
    source: 'auto',
    online: true,
    azureRegion: null,
    azureToken: null,
    azureExp: 0,
    cache: {},
    onStatus: null,

    uuid: function () {
      return 'xxxxxxxxxxxx4xxxyxxxxxxxxxxxxxxx'.replace(/[xy]/g, function (c) {
        var r = Math.random() * 16 | 0;
        var v = c === 'x' ? r : (r & 0x3 | 0x8);
        return v.toString(16);
      });
    },
    b64ToBytes: function (b64) {
      var bin = atob(b64);
      var out = new Uint8Array(bin.length);
      for (var i = 0; i < bin.length; i++) out[i] = bin.charCodeAt(i);
      return out;
    },
    bytesToB64: function (bytes) {
      var bin = '';
      for (var i = 0; i < bytes.length; i++) bin += String.fromCharCode(bytes[i]);
      return btoa(bin);
    },
    msDate: function () {
      var d = new Date();
      var days = ['Sun','Mon','Tue','Wed','Thu','Fri','Sat'];
      var months = ['Jan','Feb','Mar','Apr','May','Jun','Jul','Aug','Sep','Oct','Nov','Dec'];
      var p = function (n) { return (n < 10 ? '0' : '') + n; };
      return (days[d.getUTCDay()] + ', ' + p(d.getUTCDate()) + ' ' + months[d.getUTCMonth()] + ' ' +
        d.getUTCFullYear() + ' ' + p(d.getUTCHours()) + ':' + p(d.getUTCMinutes()) + ':' + p(d.getUTCSeconds()) + ' GMT').toLowerCase();
    },
    msSignature: function (urlStr) {
      var url = urlStr.split('://')[1];
      var encodedUrl = encodeURIComponent(url);
      var uid = this.uuid();
      var fd = this.msDate();
      var bytesToSign = ('MSTranslatorAndroidApp' + encodedUrl + fd + uid).toLowerCase();
      var key = this.b64ToBytes('oik6PdDdMnOXemTbwvMn9de/h9lFnfBaCWbGMMZqqoSaQaqUOqjVGm5NqsmjcBI1x+sS9ugjB55HEJWRiFXYFw==');
      var sig = Sha256.hmac(key, bytesToSign);
      return 'MSTranslatorAndroidApp::' + this.bytesToB64(sig) + '::' + fd + '::' + uid;
    },
    msEndpoint: function () {
      var self = this;
      var now = Math.floor(Date.now() / 1000);
      if (self.azureToken && now < self.azureExp - 180) return Promise.resolve(true);
      var url = 'https://dev.microsofttranslator.com/apps/endpoint?api-version=1.0';
      return fetch(url, {
        method: 'POST',
        headers: {
          'Accept-Language': 'zh-Hans',
          'X-ClientVersion': '4.0.530a 5fe1dc6c',
          'X-UserId': '0f04d16a175c411e',
          'X-HomeGeographicRegion': 'zh-Hans-CN',
          'X-ClientTraceId': self.uuid(),
          'X-MT-Signature': self.msSignature(url),
          'User-Agent': 'Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/127.0.0.0 Safari/537.36 Edg/127.0.0.0',
          'Content-Type': 'application/json; charset=utf-8'
        }
      }).then(function (r) {
        if (!r.ok) throw new Error('token ' + r.status);
        return r.json();
      }).then(function (d) {
        if (!d || !d.r || !d.t) throw new Error('empty token');
        self.azureRegion = d.r;
        self.azureToken = d.t;
        try {
          var payload = d.t.split('.')[1].replace(/-/g, '+').replace(/_/g, '/');
          while (payload.length % 4 !== 0) payload += '=';
          self.azureExp = JSON.parse(decodeURIComponent(escape(atob(payload)))).exp || 0;
        } catch (e) { self.azureExp = 0; }
        return true;
      });
    },
    escapeXml: function (t) {
      return String(t).replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/>/g, '&gt;')
        .replace(/"/g, '&quot;').replace(/'/g, '&apos;');
    },
    azureTts: function (text) {
      var self = this;
      return self.msEndpoint().then(function () {
        var url = 'https://' + self.azureRegion + '.tts.speech.microsoft.com/cognitiveservices/v1';
        var ssml = '<speak xmlns="http://www.w3.org/2001/10/synthesis" xmlns:mstts="http://www.w3.org/2001/mstts" version="1.0" xml:lang="zh-CN"> <voice name="' + self.voice + '"> <mstts:express-as style="general" styledegree="2.0" role="default"> <prosody rate="+0%" pitch="+0Hz" volume="+0%">' + self.escapeXml(text) + '</prosody> </mstts:express-as> </voice> </speak>';
        return fetch(url, {
          method: 'POST',
          headers: {
            'Authorization': self.azureToken,
            'Content-Type': 'application/ssml+xml',
            'X-Microsoft-OutputFormat': 'audio-24khz-48kbitrate-mono-mp3',
            'User-Agent': 'Mozilla/5.0'
          },
          body: ssml
        }).then(function (r) {
          if (!r.ok) throw new Error('speech ' + r.status);
          return r.blob();
        });
      });
    },
    baiduTts: function (text) {
      var url = 'https://fanyi.baidu.com/gettts?lan=zh&text=' + encodeURIComponent(text) + '&spd=5&source=web';
      return fetch(url, {
        headers: { 'User-Agent': 'Mozilla/5.0', 'Referer': 'https://fanyi.baidu.com/' }
      }).then(function (r) {
        if (!r.ok) throw new Error('baidu ' + r.status);
        return r.blob();
      });
    },
    voiceDisplay: function () {
      var v = this.voice;
      if (v.indexOf('Yunxi') >= 0) return '云希';
      if (v.indexOf('Yunyang') >= 0) return '云扬';
      return '晓晓';
    },
    status: function (s) {
      if (this.onStatus) { try { this.onStatus(s); } catch (e) {} }
    },
    speak: function (text) {
      var self = this;
      if (!text) return Promise.resolve();
      var key = text + '|' + self.voice;
      if (self.cache[key]) return self.play(self.cache[key]);
      var attempt = function () {
        if (!self.online || self.source === 'off') return Promise.resolve('local');
        if (self.source === 'baidu') {
          return self.baiduTts(text).then(function (b) { return { blob: b, tag: 'baidu' }; }).catch(function () { return { tag: 'local' }; });
        }
        if (self.source === 'azure') {
          return self.azureTts(text).then(function (b) { return { blob: b, tag: 'azure' }; }).catch(function () { return { tag: 'local' }; });
        }
        // auto: azure -> baidu -> local
        return self.azureTts(text).then(function (b) { return { blob: b, tag: 'azure' }; }).catch(function () {
          return self.baiduTts(text).then(function (b) { return { blob: b, tag: 'baidu' }; }).catch(function () { return { tag: 'local' }; });
        });
      };
      self.status('♪ 正在合成自然语音...');
      return attempt().then(function (res) {
        if (res.tag === 'local') {
          self.localSpeak(text);
          self.status('♪ 本地语音（离线/备用）');
          return;
        }
        self.cache[key] = res.blob;
        self.status(res.tag === 'azure' ? '♪ 微软神经语音（' + self.voiceDisplay() + '）' : '♪ 百度在线语音');
        return self.play(res.blob);
      });
    },
    play: function (blob) {
      return new Promise(function (resolve) {
        if (typeof Audio === 'undefined') { resolve(); return; }
        var url;
        try { url = URL.createObjectURL(blob); } catch (e) { resolve(); return; }
        var a = new Audio(url);
        var done = function () { try { URL.revokeObjectURL(url); } catch (e) {} resolve(); };
        a.onended = done;
        a.onerror = done;
        try { var p = a.play(); if (p && p.catch) p.catch(done); } catch (e) { done(); }
      });
    },
    localSpeak: function (text) {
      try {
        if (typeof speechSynthesis === 'undefined') return;
        speechSynthesis.cancel();
        var u = new SpeechSynthesisUtterance(text);
        u.lang = 'zh-CN';
        u.rate = 1;
        var v = this.pickVoice();
        if (v) u.voice = v;
        speechSynthesis.speak(u);
      } catch (e) {}
    },
    pickVoice: function () {
      try {
        var voices = speechSynthesis.getVoices();
        for (var i = 0; i < voices.length; i++) {
          var lg = voices[i].lang || '';
          if (lg.toLowerCase().indexOf('zh') === 0) return voices[i];
        }
      } catch (e) {}
      return null;
    },
    // 预热：仅合成缓存、不播放
    warmup: function (names) {
      var self = this;
      var i = 0;
      var next = function () {
        if (i >= names.length) return;
        var name = names[i++];
        var key = name + '|' + self.voice;
        if (self.cache[key]) { next(); return; }
        var attempt = function () {
          if (!self.online || self.source === 'off') return Promise.resolve(false);
          if (self.source === 'baidu') return self.baiduTts(name).then(function (b) { self.cache[key] = b; return true; }).catch(function () { return false; });
          if (self.source === 'azure') return self.azureTts(name).then(function (b) { self.cache[key] = b; return true; }).catch(function () { return false; });
          return self.azureTts(name).then(function (b) { self.cache[key] = b; return true; }).catch(function () {
            return self.baiduTts(name).then(function (b) { self.cache[key] = b; return true; }).catch(function () { return false; });
          });
        };
        attempt().then(function () { setTimeout(next, 120); });
      };
      setTimeout(next, 300);
    }
  };

  // ---------- 状态与持久化 ----------
  var STORE_KEY = 'lucky_picker_v1';
  var state = {
    classes: null,
    students: null,
    classId: null,
    gender: 'all',
    noRepeat: true,
    blocked: [],
    pool: [],
    lastPicked: null,
    lastMulti: [],
    hint: '点击下方按钮开始抽取'
  };
  function loadStore() {
    try {
      var raw = localStorage.getItem(STORE_KEY);
      if (raw) {
        var o = JSON.parse(raw);
        if (o && o.students && o.classes) {
          state.students = o.students;
          state.classes = o.classes;
        }
        if (o && o.source) TTS.source = o.source;
        if (o && o.voice) TTS.voice = o.voice;
        if (o && typeof o.online === 'boolean') TTS.online = o.online;
      }
    } catch (e) {}
  }
  function saveStore() {
    try {
      localStorage.setItem(STORE_KEY, JSON.stringify({
        students: state.students,
        classes: state.classes,
        source: TTS.source,
        voice: TTS.voice,
        online: TTS.online
      }));
    } catch (e) {}
  }

  // ---------- 界面 ----------
  var app = null;
  var overlays = [];

  function showOverlay(html) {
    var o = document.createElement('div');
    o.className = 'overlay';
    o.innerHTML = html;
    document.body.appendChild(o);
    overlays.push(o);
    return o;
  }
  function closeOverlay(o) {
    if (o && o.parentNode) o.parentNode.removeChild(o);
    var idx = overlays.indexOf(o);
    if (idx >= 0) overlays.splice(idx, 1);
  }
  function esc(s) {
    return String(s == null ? '' : s).replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/>/g, '&gt;').replace(/"/g, '&quot;');
  }

  function classIdsSorted() {
    var ids = [];
    var seen = {};
    for (var i = 0; i < state.students.length; i++) {
      var cid = state.students[i].classId;
      if (cid && !seen[cid]) { seen[cid] = true; ids.push(cid); }
    }
    ids.sort(function (a, b) { return (parseInt(a, 10) || 999) - (parseInt(b, 10) || 999); });
    return ids;
  }
  function className(id) {
    return (state.classes && state.classes[id]) || (id + '班');
  }
  function candidates() {
    return state.students.filter(function (s) {
      if (s.classId !== state.classId) return false;
      if (state.gender === 'male' && s.gender !== '男') return false;
      if (state.gender === 'female' && s.gender !== '女') return false;
      if (state.blocked.indexOf(s.name) >= 0) return false;
      return true;
    });
  }
  function refreshPool() {
    state.pool = candidates();
    if (!state.pool.length) state.hint = '※ 当前无候选人，请调整筛选或屏蔽';
    else state.hint = '√ 条件已更新，剩余池已刷新';
  }

  // 主界面
  function renderMain() {
    app.innerHTML =
      '<h1>YBH幸运摇人器</h1>' +
      '<div class="sub">智能抽人 · 不重复模式 · 屏蔽 · 连抽 · 语音播报 · 26H2 Build 15</div>' +
      '<div class="banner">' +
      '  <span class="pill" id="classBadge">● 当前班级：-</span>' +
      '  <span class="row"><button class="btn btn-ghost" id="btnEditor">名单管理</button>' +
      '  <button class="btn btn-ghost" id="btnSettings">语音设置</button>' +
      '  <a class="btn btn-blue" style="text-decoration:none;" href="https://lr.yibianhui.cn/" target="_blank" rel="noopener">⬇ 下载客户端</a></span>' +
      '</div>' +
      '<div class="panel">' +
      '  <div class="panel-title">筛选</div>' +
      '  <div class="row">' +
      '    <select id="classSelect"></select>' +
      '    <div class="gender-group">' +
      '      <button class="gender-btn on" data-g="all" type="button">都抽</button>' +
      '      <button class="gender-btn" data-g="male" type="button">男生</button>' +
      '      <button class="gender-btn" data-g="female" type="button">女生</button>' +
      '    </div>' +
      '    <span class="switch-row"><span class="switch on" id="noRepeat"></span>不重复模式</span>' +
      '  </div>' +
      '</div>' +
      '<div class="result-area">' +
      '  <div class="voice-badge" id="voiceBadge">♪ 语音引擎加载中...</div>' +
      '  <div class="picked-name" id="pickedName">——</div>' +
      '  <div class="multi-names" id="multiNames"></div>' +
      '  <div class="hint" id="hint">点击下方按钮开始抽取</div>' +
      '  <button class="btn btn-ghost" id="resetPool" style="margin-top:6px;">重置不重复池</button>' +
      '</div>' +
      '<div class="action-row">' +
      '  <button class="btn btn-primary" id="pickBtn" type="button">抽一人</button>' +
      '  <button class="btn btn-blue" id="multiBtn" type="button">连抽五人</button>' +
      '</div>' +
      '<div class="blocklist-area">' +
      '  <div class="blocklist-title">屏蔽名单</div>' +
      '  <div class="block-control"><input type="text" id="blockInput" placeholder="输入学生姓名"><button class="btn btn-primary" id="addBlockBtn" type="button">屏蔽此人</button></div>' +
      '  <div id="blockList"></div>' +
      '  <div class="note">名单可在「名单管理」中编辑、导入 Excel/CSV；数据保存在本机。</div>' +
      '</div>';

    // 班级下拉
    var sel = document.getElementById('classSelect');
    var ids = classIdsSorted();
    sel.innerHTML = '';
    for (var i = 0; i < ids.length; i++) {
      var op = document.createElement('option');
      op.value = ids[i];
      op.textContent = className(ids[i]);
      if (ids[i] === state.classId) op.selected = true;
      sel.appendChild(op);
    }
    sel.addEventListener('change', function () {
      state.classId = sel.value;
      refreshPool();
      updateBadge();
      renderBlocklist();
      TTS.warmup(warmNames());
    });

    // 性别
    var gbs = app.querySelectorAll('.gender-btn');
    for (var gi = 0; gi < gbs.length; gi++) {
      gbs[gi].addEventListener('click', function () {
        state.gender = this.getAttribute('data-g');
        for (var k = 0; k < gbs.length; k++) gbs[k].classList.toggle('on', gbs[k] === this);
        refreshPool();
      });
    }

    // 不重复开关
    var sw = document.getElementById('noRepeat');
    sw.addEventListener('click', function () {
      state.noRepeat = !state.noRepeat;
      sw.classList.toggle('on', state.noRepeat);
      refreshPool();
    });

    document.getElementById('pickBtn').addEventListener('click', pickOne);
    document.getElementById('multiBtn').addEventListener('click', function () { pickMultiple(5); });
    document.getElementById('resetPool').addEventListener('click', function () {
      if (confirm('确定要重置不重复池吗？')) {
        refreshPool();
        state.hint = '√ 不重复池已重置，可以开始抽取';
        renderResult();
      }
    });
    document.getElementById('blockInput').addEventListener('keydown', function (e) {
      if (e.key === 'Enter') addBlock();
    });
    document.getElementById('addBlockBtn').addEventListener('click', addBlock);
    document.getElementById('btnEditor').addEventListener('click', function () { openEditor(); });
    document.getElementById('btnSettings').addEventListener('click', function () { openEditor('settings'); });

    updateBadge();
    renderBlocklist();
    renderResult();
  }

  function updateBadge() {
    var b = document.getElementById('classBadge');
    if (b) b.textContent = '● 当前班级：' + className(state.classId);
  }

  function renderResult() {
    var pn = document.getElementById('pickedName');
    var mn = document.getElementById('multiNames');
    var ht = document.getElementById('hint');
    if (!pn) return;
    if (state.lastMulti && state.lastMulti.length) {
      pn.textContent = '';
      mn.innerHTML = '';
      for (var i = 0; i < state.lastMulti.length; i++) {
        var c = document.createElement('span');
        c.className = 'chip';
        c.textContent = state.lastMulti[i].name;
        mn.appendChild(c);
      }
    } else {
      pn.textContent = state.lastPicked || '——';
      mn.innerHTML = '';
    }
    ht.textContent = state.hint;
  }

  function renderBlocklist() {
    var box = document.getElementById('blockList');
    if (!box) return;
    box.innerHTML = '';
    if (!state.blocked.length) {
      box.innerHTML = '<span class="muted">暂无屏蔽，可输入姓名添加</span>';
      return;
    }
    for (var i = 0; i < state.blocked.length; i++) {
      (function (name) {
        var t = document.createElement('span');
        t.className = 'tag';
        t.textContent = name;
        var b = document.createElement('button');
        b.textContent = '×';
        b.addEventListener('click', function () {
          state.blocked = state.blocked.filter(function (x) { return x !== name; });
          renderBlocklist();
          refreshPool();
        });
        t.appendChild(b);
        box.appendChild(t);
      })(state.blocked[i]);
    }
  }

  function addBlock() {
    var inp = document.getElementById('blockInput');
    var name = (inp.value || '').trim();
    if (!name) return;
    var exists = false;
    for (var i = 0; i < state.students.length; i++) if (state.students[i].name === name) { exists = true; break; }
    if (!exists) { state.hint = '未找到该学生'; renderResult(); return; }
    if (state.blocked.indexOf(name) < 0) state.blocked.push(name);
    inp.value = '';
    renderBlocklist();
    refreshPool();
    renderResult();
  }

  // ---------- 抽取与动画 ----------
  var animTimer = null;
  function animate(finals) {
    var cands = candidates();
    var ticks = 0, total = 14;
    var pn = document.getElementById('pickedName');
    var mn = document.getElementById('multiNames');
    var clear = function () { if (animTimer) { clearInterval(animTimer); animTimer = null; } };
    animTimer = setInterval(function () {
      ticks++;
      if (cands.length) {
        var rnd = cands[Math.floor(Math.random() * cands.length)].name;
        pn.textContent = rnd;
        mn.innerHTML = '';
      }
      if (ticks >= total) {
        clear();
        if (finals && finals.length > 1) {
          state.lastMulti = finals;
          state.lastPicked = null;
        } else {
          state.lastPicked = finals[0] ? finals[0].name : null;
          state.lastMulti = [];
        }
        renderResult();
        speakNames(finals.map(function (s) { return s.name; }));
        flashBall(finals.map(function (s) { return s.name; }).join('、'));
      }
    }, 55);
  }
  function pickOne() {
    var cands = candidates();
    if (state.noRepeat && !state.pool.length && cands.length) {
      if (!confirm('当前不重复池已空，是否重置并继续抽取？')) return;
      refreshPool();
    }
    var source = state.noRepeat ? state.pool : cands;
    if (!source.length) { state.hint = '※ 无候选人，无法抽取'; renderResult(); return; }
    var picked = Core.pickOne(source);
    if (state.noRepeat) state.pool = state.pool.filter(function (s) { return s.name !== picked.name; });
    if (state.noRepeat && !state.pool.length) state.hint = '★ 抽中 ' + picked.name + '！剩余池已空，下次将提示重置。';
    else state.hint = '★ 抽中 ' + picked.name + '（' + className(state.classId) + '）';
    animate([picked]);
  }
  function pickMultiple(n) {
    var cands = candidates();
    if (state.noRepeat && !state.pool.length && cands.length) {
      if (!confirm('当前不重复池已空，是否重置并继续连抽？')) return;
      refreshPool();
    }
    var source = state.noRepeat ? state.pool : cands;
    if (!source.length) { state.hint = '※ 无候选人，无法连抽'; renderResult(); return; }
    var chosen = Core.pickMany(source, n);
    if (state.noRepeat) {
      var names = {};
      for (var i = 0; i < chosen.length; i++) names[chosen[i].name] = true;
      state.pool = state.pool.filter(function (s) { return !names[s.name]; });
    }
    state.hint = state.noRepeat && !state.pool.length ? '连抽完成，池已空，下次将提示重置' : '★ 连抽 ' + chosen.length + ' 人完成';
    animate(chosen);
  }
  function speakNames(names) {
    if (!names.length) return;
    TTS.speak(names.join('、'));
  }
  function warmNames() {
    var out = [];
    for (var i = 0; i < state.students.length; i++) {
      if (state.students[i].classId === state.classId) out.push(state.students[i].name);
    }
    return out;
  }

  // ---------- 班级选择 ----------
  function showClassModal() {
    var ids = classIdsSorted();
    var grid = '';
    for (var i = 0; i < ids.length; i++) {
      (function (id) {
        grid += '<button class="btn" type="button" data-class="' + esc(id) + '">' + esc(id) + '班<small>' + esc(className(id)) + '</small></button>';
      })(ids[i]);
    }
    var o = showOverlay(
      '<div class="modal">' +
      '<h2>选择班级</h2>' +
      '<div class="m-sub">请点击要抽取的班级，进入摇人器（之后可随时切换）</div>' +
      '<div class="class-grid">' + grid + '</div>' +
      '</div>');
    var btns = o.querySelectorAll('.class-grid .btn');
    for (var k = 0; k < btns.length; k++) {
      btns[k].addEventListener('click', function () {
        state.classId = this.getAttribute('data-class');
        closeOverlay(o);
        initMain();
      });
    }
  }

  function initMain() {
    if (!state.classId) return;
    // 恢复主卡片显示（班级选择阶段为避免空白卡片露边会临时隐藏）
    if (app) app.style.display = '';
    renderMain();
    refreshPool();
    updateBadge();
    TTS.warmup(warmNames());
  }

  // ---------- 名单管理 ----------
  function openSettings() { openEditor('settings'); }
  function openEditor(initialTab) {
    var tab = initialTab || 'students';
    var o = showOverlay(
      '<div class="modal" style="max-width:640px;">' +
      '<h2>名单管理</h2>' +
      '<div class="tabs">' +
      '  <button class="tab on" data-tab="students" type="button">学生名单</button>' +
      '  <button class="tab" data-tab="classes" type="button">班级名称</button>' +
      '  <button class="tab" data-tab="settings" type="button">语音设置</button>' +
      '</div>' +
      '<div id="tabBody"></div>' +
      '<div class="modal-footer">' +
      '  <button class="btn btn-primary" id="saveBtn" type="button">保存</button>' +
      '  <button class="btn btn-ghost" id="closeBtn" type="button">关闭</button>' +
      '</div>' +
      '</div>');
    var tabs = o.querySelectorAll('.tab');
    var body = o.querySelector('#tabBody');
    function renderTab() {
      if (tab === 'students') renderStudentsTab(body, o);
      else if (tab === 'classes') renderClassesTab(body, o);
      else renderSettingsTab(body, o);
    }
    // 供导入流程调用：导入后重绘当前 tab，保证「保存」能读到最新表格
    refreshEditorTab = renderTab;
    refreshEditorBody = body;
    function clearEditorRefs() {
      refreshEditorTab = null;
      refreshEditorBody = null;
    }
    for (var i = 0; i < tabs.length; i++) {
      tabs[i].addEventListener('click', function () {
        tab = this.getAttribute('data-tab');
        for (var k = 0; k < tabs.length; k++) tabs[k].classList.toggle('on', tabs[k] === this);
        renderTab();
      });
    }
    o.querySelector('#closeBtn').addEventListener('click', function () { clearEditorRefs(); closeOverlay(o); });
    o.querySelector('#saveBtn').addEventListener('click', function () {
      // 先取快照：若当前标签页没有可读取的表格（如刚导入后 UI 被替换），
      // 保存函数会直接返回，绝不能把名单清空。
      if (tab === 'students') saveStudentsTab(body);
      else if (tab === 'classes') saveClassesTab(body);
      else saveSettingsTab();
      saveStore();
      refreshPool();
      updateBadge();
      renderBlocklist();
      renderResult();
      renderTab();
      showEditorNote('已保存并写入本机浏览器 √（建议定期点「导出」备份名单）');
    });
    renderTab();
  }

  function studentsTableHtml() {
    var h = '<div class="row" style="margin-bottom:10px;">' +
      '<button class="btn btn-ghost" id="addRow" type="button">＋ 添加学生</button>' +
      '<button class="btn btn-ghost" id="delRow" type="button">－ 删除选中</button>' +
      '<button class="btn btn-ghost" id="impBtn" type="button">导入</button>' +
      '<button class="btn btn-ghost" id="expBtn" type="button">导出备份</button>' +
      '</div>' +
      '<div class="preview-wrap" style="max-height:300px;">' +
      '<table class="grid" id="stuGrid"><thead><tr><th style="width:24px;"></th><th>姓名</th><th style="width:80px;">班级</th><th style="width:80px;">性别</th></tr></thead><tbody id="stuBody"></tbody></table>' +
      '</div>' +
      '<div class="muted">可导入 .xlsx / .csv / JSON 备份（旧版 .xls 请先另存为 .xlsx）；' +
      '名单保存在<b>本浏览器</b>，换设备或清理浏览器数据会丢失，建议定期点「导出备份」。</div>';
    return h;
  }
  function renderStudentsTab(body, o) {
    body.innerHTML = studentsTableHtml();
    var tb = body.querySelector('#stuBody');
    function fill() {
      tb.innerHTML = '';
      for (var i = 0; i < state.students.length; i++) {
        var s = state.students[i];
        var tr = document.createElement('tr');
        tr.innerHTML = '<td><input type="checkbox" class="sel"></td>' +
          '<td><input type="text" class="f-name" value="' + esc(s.name) + '"></td>' +
          '<td><input type="text" class="f-cls" value="' + esc(s.classId) + '"></td>' +
          '<td><select class="f-gender"><option value="男"' + (s.gender === '男' ? ' selected' : '') + '>男</option><option value="女"' + (s.gender === '女' ? ' selected' : '') + '>女</option><option value=""' + (s.gender ? '' : ' selected') + '>未知</option></select></td>';
        tb.appendChild(tr);
      }
    }
    fill();
    body.querySelector('#addRow').addEventListener('click', function () {
      state.students.push({ name: '', classId: state.classId || '1', gender: '' });
      fill();
    });
    body.querySelector('#delRow').addEventListener('click', function () {
      var sels = tb.querySelectorAll('.sel');
      var keep = [];
      for (var i = 0; i < state.students.length; i++) {
        if (sels[i] && sels[i].checked) continue;
        keep.push(state.students[i]);
      }
      state.students = keep;
      fill();
    });
    body.querySelector('#impBtn').addEventListener('click', function () { openImport(body, o); });
    body.querySelector('#expBtn').addEventListener('click', function () {
      downloadFile('students.json', JSON.stringify({ classes: state.classes, students: state.students }, null, 2));
    });
  }
  function saveStudentsTab(body) {
    var rows = body.querySelectorAll('#stuBody tr');
    // 安全保护：当前 UI 不是学生表格时直接返回，绝不能用空列表覆盖已有名单
    if (!rows || !rows.length) return;
    var out = [];
    for (var i = 0; i < rows.length; i++) {
      var name = (rows[i].querySelector('.f-name').value || '').trim();
      var cls = (rows[i].querySelector('.f-cls').value || '').trim();
      var gen = rows[i].querySelector('.f-gender').value || '';
      if (!name && !cls && !gen) continue;
      if (!name) { alert('第 ' + (i + 1) + ' 行：姓名不能为空'); return; }
      if (!cls) { alert('第 ' + (i + 1) + ' 行（' + name + '）：班级不能为空'); return; }
      out.push({ name: name, classId: Core.normalizeClass(cls), gender: Core.normalizeGender(gen) });
    }
    state.students = out;
    // 班级名称自动补齐
    var ids = {};
    for (var k = 0; k < out.length; k++) ids[out[k].classId] = true;
    for (var cid in ids) if (!state.classes[cid]) state.classes[cid] = cid + '班';
  }
  function renderClassesTab(body, o) {
    var h = '<div class="preview-wrap" style="max-height:260px;">' +
      '<table class="grid"><thead><tr><th>班级号</th><th>显示名称</th></tr></thead><tbody id="clsBody"></tbody></table>' +
      '</div><div class="muted">班级号与显示名称对应关系（如 19 → 示例十九班）。</div>';
    body.innerHTML = h;
    var tb = body.querySelector('#clsBody');
    var ids = classIdsSorted();
    for (var i = 0; i < ids.length; i++) {
      var tr = document.createElement('tr');
      tr.innerHTML = '<td><input type="text" class="f-id" value="' + esc(ids[i]) + '"></td>' +
        '<td><input type="text" class="f-disp" value="' + esc(state.classes[ids[i]] || (ids[i] + '班')) + '"></td>';
      tb.appendChild(tr);
    }
  }
  function saveClassesTab(body) {
    var rows = body.querySelectorAll('#clsBody tr');
    // 同上保护：没有班级表格时不覆盖
    if (!rows) return;
    var cls = {};
    for (var i = 0; i < rows.length; i++) {
      var id = (rows[i].querySelector('.f-id').value || '').trim();
      var disp = (rows[i].querySelector('.f-disp').value || '').trim();
      if (id) cls[id] = disp || (id + '班');
    }
    state.classes = cls;
  }

  // ---------- 语音设置 ----------
  function renderSettingsTab(body, o) {
    body.innerHTML =
      '<div class="setting-line"><span>使用在线语音：</span>' +
      '<span class="switch ' + (TTS.online ? 'on' : '') + '" id="swOnline"></span></div>' +
      '<div class="setting-line"><span>在线语音源：</span>' +
      '<select id="srcSel">' +
      '<option value="auto"' + (TTS.source === 'auto' ? ' selected' : '') + '>自动（微软神经语音 → 百度 → 本地）</option>' +
      '<option value="azure"' + (TTS.source === 'azure' ? ' selected' : '') + '>微软神经语音（直连）</option>' +
      '<option value="baidu"' + (TTS.source === 'baidu' ? ' selected' : '') + '>百度翻译</option>' +
      '<option value="off"' + (TTS.source === 'off' ? ' selected' : '') + '>仅本地（SAPI）</option>' +
      '</select></div>' +
      '<div class="setting-line"><span>音色：</span>' +
      '<select id="voiceSel">' +
      '<option value="zh-CN-XiaoxiaoNeural"' + (TTS.voice === 'zh-CN-XiaoxiaoNeural' ? ' selected' : '') + '>晓晓（女声，推荐）</option>' +
      '<option value="zh-CN-YunxiNeural"' + (TTS.voice === 'zh-CN-YunxiNeural' ? ' selected' : '') + '>云希（男声）</option>' +
      '<option value="zh-CN-YunyangNeural"' + (TTS.voice === 'zh-CN-YunyangNeural' ? ' selected' : '') + '>云扬（男声·新闻）</option>' +
      '</select></div>' +
      '<div class="muted">微软神经语音与 Edge TTS 同源，内置直连可用；语音会缓存，听过的名字再次抽取秒播。</div>' +
      buildBallSettingsHtml() +
      buildBootSettingsHtml();
    body.querySelector('#swOnline').addEventListener('click', function () {
      TTS.online = !TTS.online;
      this.classList.toggle('on', TTS.online);
    });
    body.querySelector('#srcSel').addEventListener('change', function () { TTS.source = this.value; });
    body.querySelector('#voiceSel').addEventListener('change', function () {
      TTS.voice = this.value;
      TTS.cache = {};
    });
    bindBallSettings(body);
    bindBootSettings(body);
  }
  function saveSettingsTab() {}

  // 悬浮球设置（所有平台均可开关）
  function buildBallSettingsHtml() {
    var on = ball.visible !== false;
    return '<div class="setting-line" style="margin-top:12px;"><span>桌面悬浮球：</span>' +
      '<span class="switch ' + (on ? 'on' : '') + '" id="swBall"></span>' +
      '<span class="muted">单击抽一人，拖动移动，长按出菜单</span></div>';
  }
  function bindBallSettings(body) {
    var sw = body.querySelector('#swBall');
    if (!sw) return;
    sw.addEventListener('click', function () {
      var on = !sw.classList.contains('on');
      sw.classList.toggle('on', on);
      if (on) showBallAgain();
      else {
        ball.visible = false;
        if (ball.el) { ball.el.parentNode.removeChild(ball.el); ball.el = null; }
        saveBallPrefs();
      }
    });
  }
  // 开机自启动（仅 Android 原生桥接可用时显示）
  function buildBootSettingsHtml() {
    var bridge = (typeof window !== 'undefined') ? window.LuckyBridge : null;
    if (!bridge || typeof bridge.isBootEnabled !== 'function') return '';
    var on = String(bridge.isBootEnabled()) === '1';
    return '<div class="setting-line"><span>开机自启动：</span>' +
      '<span class="switch ' + (on ? 'on' : '') + '" id="swBoot"></span>' +
      '<span class="muted">开机后仅显示悬浮球</span></div>';
  }
  function bindBootSettings(body) {
    var sw = body.querySelector('#swBoot');
    if (!sw) return;
    var bridge = window.LuckyBridge;
    sw.addEventListener('click', function () {
      var on = !sw.classList.contains('on');
      try {
        bridge.setBootEnabled(on);
        sw.classList.toggle('on', String(bridge.isBootEnabled()) === '1');
      } catch (e) {}
    });
  }

  // ---------- 导入 ----------
  // 编辑器当前 tab 的重绘回调（导入后必须重绘，保证「保存」能读到最新表格）
  var refreshEditorTab = null;
  var refreshEditorBody = null;

  function showEditorNote(text) {
    if (!refreshEditorBody) return;
    var body = refreshEditorBody;
    var old = body.querySelector('.ed-note');
    if (old) old.parentNode.removeChild(old);
    var d = document.createElement('div');
    d.className = 'ed-note';
    d.textContent = text;
    body.insertBefore(d, body.firstChild);
  }

  function openImport(body, o) {
    var fileInput = document.createElement('input');
    fileInput.type = 'file';
    fileInput.accept = '.xlsx,.csv,.json';
    fileInput.addEventListener('change', function () {
      var file = fileInput.files[0];
      if (!file) return;
      var reader = new FileReader();
      reader.onload = function () {
        // ① JSON 备份恢复（程序内「导出」的 students.json）
        if (/\.json$/i.test(file.name)) {
          try {
            var obj = JSON.parse(String(reader.result));
            var st = obj && obj.students ? obj.students : null;
            if (!st || !st.length) { alert('该 JSON 中没有学生数据'); return; }
            if (confirm('共读取到 ' + st.length + ' 名学生。\n确定 = 替换现有名单\n取消 = 追加到末尾')) {
              state.students = st.slice();
            } else {
              state.students = state.students.concat(st);
            }
            if (obj.classes) {
              for (var cid in obj.classes) if (!state.classes[cid]) state.classes[cid] = obj.classes[cid];
            }
            finishImport(st.length);
          } catch (e) {
            alert('JSON 解析失败：' + e.message);
          }
          return;
        }

        // ② Excel / CSV
        var rows = null;
        if (/\.xlsx$/i.test(file.name)) {
          var u8 = new Uint8Array(reader.result);
          rows = Core.parseXlsx(u8, (typeof pako !== 'undefined') ? pako : null);
          if (!rows) { alert('无法解析该 .xlsx 文件'); return; }
        } else {
          rows = Core.parseCsv(String(reader.result));
        }
        if (!rows || !rows.length) { alert('文件中没有找到数据行'); return; }
        openMapping(rows, function (students) {
          if (!students || !students.length) { alert('没有解析到有效学生'); return; }
          var mode = confirm('共解析到 ' + students.length + ' 名学生。\n确定 = 替换现有名单\n取消 = 追加到末尾');
          if (mode) state.students = students.slice();
          else state.students = state.students.concat(students);
          finishImport(students.length);
        });
      };
      if (/\.xlsx$/i.test(file.name)) reader.readAsArrayBuffer();
      else reader.readAsText(file);
    });
    fileInput.click();
  }

  /**
   * 导入收尾：补齐班级名 → 立即保存到本机 → 重绘编辑器表格 → 提示。
   * 关键：必须重绘表格，否则「保存」按钮读不到行数据，会把名单清空。
   */
  function finishImport(count) {
    var ids = {};
    for (var i = 0; i < state.students.length; i++) ids[state.students[i].classId] = true;
    for (var cid in ids) if (!state.classes[cid]) state.classes[cid] = cid + '班';

    saveStore();
    refreshPool();
    renderBlocklist();
    if (typeof refreshEditorTab === 'function') refreshEditorTab();
    showEditorNote('已导入 ' + count + ' 名学生，名单已保存到本浏览器 \u2713 建议点「导出」留一份备份。');
  }

  function openMapping(rows, onDone) {
    var cols = 0;
    for (var i = 0; i < rows.length; i++) cols = Math.max(cols, rows[i].length);
    cols = Math.max(1, Math.min(cols, 12));
    var nCol = Core.findColumn(rows, ['姓名', '名字', '名称', '学生', 'name']);
    var cCol = Core.findColumn(rows, ['班级', '班', 'class']);
    var gCol = Core.findColumn(rows, ['性别', 'gender', 'sex']);
    var hasHeader = nCol >= 0 || cCol >= 0 || gCol >= 0;
    var preview = '';
    for (var r = 0; r < Math.min(rows.length, 8); r++) {
      preview += '<tr>';
      for (var c = 0; c < cols; c++) preview += '<td>' + esc(rows[r][c] || '') + '</td>';
      preview += '</tr>';
    }
    var colOpts = '';
    for (var c2 = 0; c2 < cols; c2++) colOpts += '<option value="' + c2 + '">第' + (c2 + 1) + '列</option>';
    var o = showOverlay(
      '<div class="modal" style="max-width:620px;">' +
      '<h2>导入预览与列匹配</h2>' +
      '<div class="preview-wrap"><table class="grid"><thead><tr>' +
      (function () { var th = ''; for (var c3 = 0; c3 < cols; c3++) th += '<th>第' + (c3 + 1) + '列</th>'; return th; })() +
      '</tr></thead><tbody>' + preview + '</tbody></table></div>' +
      '<div class="setting-line"><span>姓名列：</span><select id="mapName">' + colOpts + '</select>' +
      '<span>班级列：</span><select id="mapCls">' + colOpts + '</select>' +
      '<span>性别列：</span><select id="mapGender">' + colOpts + '</select></div>' +
      '<div class="setting-line"><label><input type="checkbox" id="mapHeader"' + (hasHeader ? ' checked' : '') + '> 首行为表头（跳过第一行）</label></div>' +
      '<div class="modal-footer">' +
      '<button class="btn btn-primary" id="okBtn" type="button">确定导入</button>' +
      '<button class="btn btn-ghost" id="cancelBtn" type="button">取消</button>' +
      '</div></div>');
    var set = function (sel, v) { if (v >= 0) sel.value = String(v); };
    set(o.querySelector('#mapName'), nCol >= 0 ? nCol : 0);
    set(o.querySelector('#mapCls'), cCol >= 0 ? cCol : (cols > 1 ? 1 : 0));
    set(o.querySelector('#mapGender'), gCol >= 0 ? gCol : (cols > 2 ? 2 : 0));
    o.querySelector('#okBtn').addEventListener('click', function () {
      var mapping = {
        header: o.querySelector('#mapHeader').checked,
        name: parseInt(o.querySelector('#mapName').value, 10),
        cls: parseInt(o.querySelector('#mapCls').value, 10),
        gender: parseInt(o.querySelector('#mapGender').value, 10)
      };
      var students = Core.buildStudents(rows, mapping);
      closeOverlay(o);
      onDone(students);
    });
    o.querySelector('#cancelBtn').addEventListener('click', function () { closeOverlay(o); });
  }

  function downloadFile(name, content) {
    try {
      var blob = new Blob([content], { type: 'application/json;charset=utf-8' });
      var url = URL.createObjectURL(blob);
      var a = document.createElement('a');
      a.href = url;
      a.download = name;
      document.body.appendChild(a);
      a.click();
      setTimeout(function () { URL.revokeObjectURL(url); }, 1000);
    } catch (e) {}
  }

  // ---------- 悬浮球（可拖动，单击抽一人，长按/右键出菜单） ----------
  var BALL_KEY = 'lucky_picker_ball_v1';
  var ball = { el: null, x: null, y: null, flashTimer: null, pressTimer: null, dragged: false };
  function loadBallPrefs() {
    try {
      var raw = localStorage.getItem(BALL_KEY);
      if (raw) {
        var o = JSON.parse(raw);
        if (o && typeof o.visible === 'boolean') ball.visible = o.visible;
        if (o && typeof o.x === 'number') ball.x = o.x;
        if (o && typeof o.y === 'number') ball.y = o.y;
      }
    } catch (e) {}
  }
  function saveBallPrefs() {
    try {
      localStorage.setItem(BALL_KEY, JSON.stringify({
        visible: ball.visible !== false, x: ball.x, y: ball.y
      }));
    } catch (e) {}
  }
  function ballDefaultPos() {
    return { x: Math.max(12, window.innerWidth - 92), y: Math.round(window.innerHeight * 0.62) };
  }
  function ensureBall() {
    if (ball.el || !document.body) return;
    loadBallPrefs();
    if (ball.visible === false) return;   // 用户隐藏过则不创建
    var b = document.createElement('div');
    b.id = 'lp-ball';
    b.innerHTML = '<span class="lp-ball-face">摇</span>';
    document.body.appendChild(b);
    ball.el = b;

    var pos = ballDefaultPos();
    if (ball.x == null) { ball.x = pos.x; ball.y = pos.y; }
    clampBall();
    ball.el.style.left = ball.x + 'px';
    ball.el.style.top = ball.y + 'px';

    var startX = 0, startY = 0, baseX = 0, baseY = 0, pressing = false;
    b.addEventListener('pointerdown', function (e) {
      if (e.button === 2) return;
      pressing = true; ball.dragged = false;
      startX = e.clientX; startY = e.clientY; baseX = ball.x; baseY = ball.y;
      try { b.setPointerCapture(e.pointerId); } catch (er) {}
      // 长按弹出菜单（触屏）
      if (ball.pressTimer) clearTimeout(ball.pressTimer);
      ball.pressTimer = setTimeout(function () {
        if (pressing && !ball.dragged) { pressing = false; showBallMenu(); }
      }, 550);
    });
    b.addEventListener('pointermove', function (e) {
      if (!pressing) return;
      var dx = e.clientX - startX, dy = e.clientY - startY;
      if (Math.abs(dx) + Math.abs(dy) > 8) { ball.dragged = true; clearTimeout(ball.pressTimer); }
      if (ball.dragged) {
        ball.x = baseX + dx; ball.y = baseY + dy;
        clampBall();
        ball.el.style.left = ball.x + 'px';
        ball.el.style.top = ball.y + 'px';
      }
    });
    b.addEventListener('pointerup', function (e) {
      clearTimeout(ball.pressTimer);
      if (!pressing) return;
      pressing = false;
      if (ball.dragged) { saveBallPrefs(); return; }
      pickFromBall();
    });
    b.addEventListener('pointercancel', function () {
      clearTimeout(ball.pressTimer); pressing = false;
    });
    b.addEventListener('contextmenu', function (e) {
      e.preventDefault();
      showBallMenu();
    });
  }
  function clampBall() {
    var w = 64, h = 64;
    ball.x = Math.max(-w / 2, Math.min(ball.x, window.innerWidth - w / 2));
    ball.y = Math.max(0, Math.min(ball.y, window.innerHeight - h / 2));
  }
  function pickFromBall() {
    if (!state.classId) return;   // 尚未选择班级（还在班级选择页）
    pickOne();
  }
  function flashBall(names) {
    if (!ball.el || !names) return;
    var face = ball.el.querySelector('.lp-ball-face');
    if (!face) return;
    var text = names.length > 4 ? names.slice(0, 3) + '…' : names;
    face.textContent = text;
    ball.el.classList.add('flash');
    if (ball.flashTimer) clearTimeout(ball.flashTimer);
    ball.flashTimer = setTimeout(function () {
      face.textContent = '摇';
      ball.el.classList.remove('flash');
    }, 3000);
  }
  function showBallMenu() {
    var old = document.getElementById('lpBallMenu');
    if (old) { old.parentNode.removeChild(old); return; }
    var m = document.createElement('div');
    m.id = 'lpBallMenu';
    m.innerHTML =
      '<div class="lp-ball-item" data-act="show">打开主界面</div>' +
      '<div class="lp-ball-item" data-act="one">抽一人</div>' +
      '<div class="lp-ball-item" data-act="multi">连抽五人</div>' +
      '<div class="lp-ball-item" data-act="reset">重置不重复池</div>' +
      '<div class="lp-ball-item" data-act="hide">隐藏悬浮球</div>';
    document.body.appendChild(m);
    m.addEventListener('click', function (e) {
      var act = e.target.getAttribute && e.target.getAttribute('data-act');
      if (!act) return;
      m.parentNode.removeChild(m);
      if (act === 'hide') {
        ball.visible = false;
        if (ball.el) { ball.el.parentNode.removeChild(ball.el); ball.el = null; }
        saveBallPrefs();
        state.hint = '悬浮球已隐藏，可在「语音设置」中重新开启';
        renderResult();
        return;
      }
      if (!state.classId) return;
      if (act === 'show') { window.scrollTo(0, 0); }
      else if (act === 'one') { pickOne(); }
      else if (act === 'multi') { pickMultiple(5); }
      else if (act === 'reset') {
        refreshPool();
        state.hint = '√ 不重复池已重置，可以开始抽取';
        renderResult();
      }
    });
    setTimeout(function () {
      document.addEventListener('click', function once(ev) {
        if (m.parentNode && !m.contains(ev.target)) { m.parentNode.removeChild(m); }
        document.removeEventListener('click', once);
      });
    }, 0);
  }
  function showBallAgain() {
    ball.visible = true;
    saveBallPrefs();
    ensureBall();
  }

  // ---------- 启动 ----------
  function init() {
    app = document.getElementById('app');
    loadStore();
    if (!state.students || !state.students.length) {
      state.students = DEFAULT_DATA.students.slice();
      state.classes = {};
      for (var k in DEFAULT_DATA.classes) state.classes[k] = DEFAULT_DATA.classes[k];
    }
    TTS.onStatus = function (s) {
      var b = document.getElementById('voiceBadge');
      if (b) b.textContent = s;
    };
    var vc = TTS.pickVoice();
    TTS.status('♪ 语音：' + (TTS.online ? '在线神经语音优先 · ' : '本地语音模式 · ') + (vc ? '本地语音备用就绪' : '本地语音备用'));
    // 先渲染主界面（默认班级），避免班级选择遮罩下露出空白卡片形成的“白条”
    state.classId = classIdsSorted()[0] || '1';
    renderMain();
    ensureBall();
    showClassModal();
  }

  if (typeof window !== 'undefined') window.LuckyPicker = { Core: Core, TTS: TTS, Sha256: Sha256, version: '26H2 Build 15', showBallAgain: showBallAgain };
  if (typeof module !== 'undefined' && module.exports) {
    module.exports = { Core: Core, TTS: TTS, Sha256: Sha256, DEFAULT_DATA: DEFAULT_DATA };
  }
  if (typeof document !== 'undefined' && document.getElementById) init();
})();
