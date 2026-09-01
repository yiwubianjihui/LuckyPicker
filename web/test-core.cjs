// Web 核心 Node 测试：SHA-256/HMAC、CSV/XLSX 解析、构建名单、TTS 在线链路
const assert = require('assert');
const crypto = require('crypto');
const pako = require('E:/dsh/LuckyPicker/web/node_modules/pako/dist/pako.cjs.js');
const app = require('E:/dsh/LuckyPicker/web/app.js');
const { Core, TTS, Sha256, DEFAULT_DATA } = app;
let pass = 0, fail = 0;
function check(name, ok) { console.log((ok ? 'PASS  ' : 'FAIL  ') + name); ok ? pass++ : fail++; }

(async () => {
  // 1) SHA-256 正确性（对比 node crypto）
  const msg = 'MSTranslatorAndroidAppdev.microsofttranslator.com%2Fapps%2Fendpoint%3Fapi-version%3D1.0fri, 14 aug 2026 07:00:00 gmt0123456789abcdef';
  const key = Buffer.from('oik6PdDdMnOXemTbwvMn9de/h9lFnfBaCWbGMMZqqoSaQaqUOqjVGm5NqsmjcBI1x+sS9ugjB55HEJWRiFXYFw==', 'base64');
  const expected = crypto.createHmac('sha256', key).update(msg, 'utf8').digest();
  const got = Buffer.from(Sha256.hmac(Array.from(key), msg));
  check('hmac matches node crypto', got.equals(expected));
  const h1 = Buffer.from(Sha256.hash(Sha256.toBytes('abc'))).toString('hex');
  check('sha256 abc', h1 === 'ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad');

  // 2) 规范化
  check('normalizeClass', Core.normalizeClass('19班') === '19' && Core.normalizeClass(' 20 ') === '20');
  check('normalizeGender', Core.normalizeGender('男生') === '男' && Core.normalizeGender('女') === '女' && Core.normalizeGender('') === '');

  // 3) CSV
  const csv = '姓名,班级,性别\r\n张三,19,男\r\n"李,四",18,女\r\n';
  const rows = Core.parseCsv(csv);
  check('csv rows', rows.length === 3 && rows[1][0] === '张三' && rows[2][0] === '李,四');
  check('csv findColumn', Core.findColumn(rows, ['姓名']) === 0 && Core.findColumn(rows, ['性别']) === 2);

  // 4) XLSX（用 pako 构造测试文件）
  const { createXlsx } = require('E:/dsh/LuckyPicker/.tmp/xlsx-make.cjs');
  const buf = createXlsx();
  const xrows = Core.parseXlsx(new Uint8Array(buf), pako);
  check('xlsx rows', xrows && xrows.length === 3 && xrows[1][0] === '张三' && xrows[1][1] === '19' && xrows[2][2] === '女');

  // 5) buildStudents 列映射
  const students = Core.buildStudents(xrows, { header: true, name: 0, cls: 1, gender: 2 });
  check('buildStudents', students.length === 2 && students[0].name === '张三' && students[0].classId === '19' && students[0].gender === '男');

  // 6) pickMany 不重复
  const pool = [{name:'a'},{name:'b'},{name:'c'},{name:'d'},{name:'e'}];
  const picked = Core.pickMany(pool, 3);
  check('pickMany distinct', picked.length === 3 && new Set(picked.map(p=>p.name)).size === 3);
  check('pickOne', pool.includes(Core.pickOne(pool)));

  // 7) 默认数据：仓库内置虚构示例名单（50 人），不含真实信息
  check('default data is demo roster', DEFAULT_DATA.students.length === 50);

  // 8) TTS 在线链路（微软神经语音）
  try {
    const blob = await TTS.azureTts('测试语音');
    check('azure tts blob', blob && blob.size > 1000);
  } catch (e) { check('azure tts blob (EX: ' + e.message + ')', false); }
  try {
    await TTS.speak('张铭乾');
    check('tts.speak ok', true);
  } catch (e) { check('tts.speak (EX: ' + e.message + ')', false); }

  console.log('----');
  console.log(fail === 0 ? 'ALL PASS (' + pass + ')' : fail + ' FAILURES of ' + (pass + fail));
  process.exit(fail === 0 ? 0 : 1);
})();
