// Web 核心 Node 测试；--online 时额外验证在线 TTS。
const crypto = require('crypto');
const pako = require('pako');
const app = require('./app.js');
const { Core, TTS, Sha256, DEFAULT_DATA } = app;
let pass = 0, fail = 0;
function check(name, ok) { console.log((ok ? 'PASS  ' : 'FAIL  ') + name); ok ? pass++ : fail++; }

function entry(name, content) {
  const raw = Buffer.from(content, 'utf8');
  const comp = pako.deflateRaw(raw);
  const nameBuf = Buffer.from(name, 'utf8');
  const out = Buffer.alloc(30 + nameBuf.length + comp.length);
  out.writeUInt32LE(0x04034b50, 0);
  out.writeUInt16LE(20, 4);
  out.writeUInt16LE(0x0800, 6);
  out.writeUInt16LE(8, 8);
  out.writeUInt32LE(0, 10);
  out.writeUInt32LE(comp.length, 18);
  out.writeUInt32LE(raw.length, 22);
  out.writeUInt16LE(nameBuf.length, 26);
  out.writeUInt16LE(0, 28);
  nameBuf.copy(out, 30);
  Buffer.from(comp).copy(out, 30 + nameBuf.length);
  return out;
}

function createXlsx() {
  return Buffer.concat([
    entry('[Content_Types].xml', '<Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types"><Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/><Default Extension="xml" ContentType="application/xml"/></Types>'),
    entry('xl/workbook.xml', '<workbook xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main" xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships"><sheets><sheet name="Sheet1" sheetId="1" r:id="rId1"/></sheets></workbook>'),
    entry('xl/_rels/workbook.xml.rels', '<Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships"><Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet" Target="worksheets/sheet1.xml"/></Relationships>'),
    entry('xl/sharedStrings.xml', '<sst xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main"><si><t>姓名</t></si><si><t>班级</t></si><si><t>性别</t></si><si><t>张三</t></si><si><t>男</t></si><si><t>女</t></si></sst>'),
    entry('xl/worksheets/sheet1.xml', '<worksheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main"><sheetData><row r="1"><c r="A1" t="s"><v>0</v></c><c r="B1" t="s"><v>1</v></c><c r="C1" t="s"><v>2</v></c></row><row r="2"><c r="A2" t="s"><v>3</v></c><c r="B2"><v>19</v></c><c r="C2" t="s"><v>4</v></c></row><row r="3"><c r="A3" t="inlineStr"><is><t>李四</t></is></c><c r="B3"><v>18</v></c><c r="C3" t="s"><v>5</v></c></row></sheetData></worksheet>')
  ]);
}

(async () => {
  const msg = 'MSTranslatorAndroidAppdev.microsofttranslator.com%2Fapps%2Fendpoint%3Fapi-version%3D1.0fri, 14 aug 2026 07:00:00 gmt0123456789abcdef';
  const key = Buffer.from('oik6PdDdMnOXemTbwvMn9de/h9lFnfBaCWbGMMZqqoSaQaqUOqjVGm5NqsmjcBI1x+sS9ugjB55HEJWRiFXYFw==', 'base64');
  const expected = crypto.createHmac('sha256', key).update(msg, 'utf8').digest();
  const got = Buffer.from(Sha256.hmac(Array.from(key), msg));
  check('hmac matches node crypto', got.equals(expected));
  const h1 = Buffer.from(Sha256.hash(Sha256.toBytes('abc'))).toString('hex');
  check('sha256 abc', h1 === 'ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad');

  check('normalizeClass', Core.normalizeClass('19班') === '19' && Core.normalizeClass(' 20 ') === '20');
  check('normalizeGender', Core.normalizeGender('男生') === '男' && Core.normalizeGender('女') === '女' && Core.normalizeGender('') === '');

  const csv = '姓名,班级,性别\r\n张三,19,男\r\n"李,四",18,女\r\n';
  const rows = Core.parseCsv(csv);
  check('csv rows', rows.length === 3 && rows[1][0] === '张三' && rows[2][0] === '李,四');
  check('csv findColumn', Core.findColumn(rows, ['姓名']) === 0 && Core.findColumn(rows, ['性别']) === 2);

  const xrows = Core.parseXlsx(new Uint8Array(createXlsx()), pako);
  check('xlsx rows', xrows && xrows.length === 3 && xrows[1][0] === '张三' && xrows[1][1] === '19' && xrows[2][2] === '女');

  const students = Core.buildStudents(xrows, { header: true, name: 0, cls: 1, gender: 2 });
  check('buildStudents', students.length === 2 && students[0].name === '张三' && students[0].classId === '19' && students[0].gender === '男');

  const pool = [{ name: 'a' }, { name: 'b' }, { name: 'c' }, { name: 'd' }, { name: 'e' }];
  const picked = Core.pickMany(pool, 3);
  check('pickMany distinct', picked.length === 3 && new Set(picked.map(p => p.name)).size === 3);
  check('pickOne', pool.includes(Core.pickOne(pool)));
  check('default data is demo roster', DEFAULT_DATA.students.length === 50);

  if (process.argv.includes('--online')) {
    try {
      const blob = await TTS.azureTts('测试语音');
      check('azure tts blob', blob && blob.size > 1000);
    } catch (e) { check('azure tts blob (EX: ' + e.message + ')', false); }
    try {
      await TTS.speak('张铭乾');
      check('tts.speak ok', true);
    } catch (e) { check('tts.speak (EX: ' + e.message + ')', false); }
  } else {
    console.log('SKIP  online TTS (use npm run test:online)');
  }

  console.log('----');
  console.log(fail === 0 ? 'ALL PASS (' + pass + ')' : fail + ' FAILURES of ' + (pass + fail));
  process.exit(fail === 0 ? 0 : 1);
})();
