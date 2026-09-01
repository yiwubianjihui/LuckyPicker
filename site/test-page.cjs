const { JSDOM } = require('E:/dsh/LuckyPicker/web/node_modules/jsdom');
const fs = require('fs');
const path = require('path');

const html = fs.readFileSync(path.join(__dirname, 'index.html'), 'utf8');
let pass = 0, fail = 0;
function check(name, ok) { console.log((ok ? 'PASS  ' : 'FAIL  ') + name); ok ? pass++ : fail++; }

const dom = new JSDOM(html, { runScripts: 'dangerously', url: 'file://' + __dirname.replace(/\\/g, '/') + '/index.html' });
const doc = dom.window.document;

setTimeout(() => {
  // 1) 5 张版本卡
  const cards = doc.querySelectorAll('.vcard');
  check('5 version cards', cards.length === 5);
  // 2) 默认选中 Windows 安装版
  check('default title', doc.getElementById('dTitle').textContent.indexOf('Windows 安装版') >= 0);
  check('default size', doc.getElementById('dMeta').textContent.indexOf('511 KB') >= 0);
  // 3) 逐个点击并校验
  const keys = ['win-install', 'win-portable', 'android', 'linux', 'web'];
  const expected = {
    'win-install': ['Windows 安装版', 'Setup.exe'],
    'win-portable': ['Windows 便携版', 'portable'],
    'android': ['Android', 'LuckyPicker.apk'],
    'linux': ['Linux', 'LuckyPicker-linux-x64.zip'],
    'web': ['Web 版', 'lucky-picker-web.zip']
  };
  let filesOk = true;
  for (let i = 0; i < keys.length; i++) {
    cards[i].dispatchEvent(new dom.window.MouseEvent('click', { bubbles: true }));
    const t = doc.getElementById('dTitle').textContent;
    const href = doc.getElementById('dBtn').getAttribute('href');
    const okTitle = t.indexOf(expected[keys[i]][0]) >= 0;
    const okHref = href && href.indexOf(expected[keys[i]][1]) >= 0;
    const filePath = path.join(__dirname, href.replace(/\//g, path.sep));
    const okFile = fs.existsSync(filePath);
    if (!okFile) filesOk = false;
    check('card ' + keys[i] + ' title+href', okTitle && okHref && okFile);
  }
  check('all download files exist', filesOk);
  // 4) 按钮文案
  check('button label', doc.getElementById('dBtn').textContent.indexOf('下载') >= 0);
  console.log('----');
  console.log(fail === 0 ? 'ALL PASS (' + pass + ')' : fail + ' FAILURES of ' + (pass + fail));
  process.exit(fail === 0 ? 0 : 1);
}, 300);
