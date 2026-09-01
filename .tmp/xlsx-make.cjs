// 生成测试 .xlsx（deflate 压缩条目）
const pako = require('E:/dsh/LuckyPicker/web/node_modules/pako/dist/pako.cjs.js');
function entry(name, content) {
  const raw = Buffer.from(content, 'utf8');
  const comp = pako.deflateRaw(raw);
  const nameBuf = Buffer.from(name, 'utf8');
  const out = Buffer.alloc(30 + nameBuf.length + comp.length);
  out.writeUInt32LE(0x04034b50, 0);
  out.writeUInt16LE(20, 4);
  out.writeUInt16LE(0x0800, 6);
  out.writeUInt16LE(8, 8);        // method deflate
  out.writeUInt32LE(0, 10);       // crc (ignored by parser)
  out.writeUInt32LE(comp.length, 18);
  out.writeUInt32LE(raw.length, 22);
  out.writeUInt16LE(nameBuf.length, 26);
  out.writeUInt16LE(0, 28);
  nameBuf.copy(out, 30);
  Buffer.from(comp).copy(out, 30 + nameBuf.length);
  return out;
}
function createXlsx() {
  const parts = [
    entry('[Content_Types].xml', '<Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types"><Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/><Default Extension="xml" ContentType="application/xml"/></Types>'),
    entry('xl/workbook.xml', '<workbook xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main" xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships"><sheets><sheet name="Sheet1" sheetId="1" r:id="rId1"/></sheets></workbook>'),
    entry('xl/_rels/workbook.xml.rels', '<Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships"><Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet" Target="worksheets/sheet1.xml"/></Relationships>'),
    entry('xl/sharedStrings.xml', '<sst xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main"><si><t>姓名</t></si><si><t>班级</t></si><si><t>性别</t></si><si><t>张三</t></si><si><t>男</t></si><si><t>女</t></si></sst>'),
    entry('xl/worksheets/sheet1.xml', '<worksheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main"><sheetData><row r="1"><c r="A1" t="s"><v>0</v></c><c r="B1" t="s"><v>1</v></c><c r="C1" t="s"><v>2</v></c></row><row r="2"><c r="A2" t="s"><v>3</v></c><c r="B2"><v>19</v></c><c r="C2" t="s"><v>4</v></c></row><row r="3"><c r="A3" t="inlineStr"><is><t>李四</t></is></c><c r="B3"><v>18</v></c><c r="C3" t="s"><v>5</v></c></row></sheetData></worksheet>')
  ];
  return Buffer.concat(parts);
}
module.exports = { createXlsx };
