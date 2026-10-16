// Writes one Resources.resw per language from scripts/strings.json.
// WinUI loads these files for the window's x:Uid text, using the Windows display language.
const fs = require("fs");
const path = require("path");

const root = path.join(__dirname, "..", "src", "Mercury.Converter.App", "Strings");
const strings = JSON.parse(fs.readFileSync(path.join(__dirname, "strings.json"), "utf8"));
const languages = { en: "en-us", es: "es-es", fr: "fr-fr", de: "de-de", "pt-BR": "pt-br" };

const escape = (text) => text.replace(/&/g, "&amp;").replace(/</g, "&lt;").replace(/>/g, "&gt;");

for (const [code, folder] of Object.entries(languages)) {
  const entries = Object.entries(strings).filter(([key]) => !key.startsWith("_"));
  const data = entries.map(([key, values]) => {
    const text = values[code] ?? values.en;
    return `  <data name="${key}" xml:space="preserve">\n    <value>${escape(text)}</value>\n  </data>`;
  });

  const xml = [
    '<?xml version="1.0" encoding="utf-8"?>',
    '<root>',
    '  <resheader name="resmimetype"><value>text/microsoft-resx</value></resheader>',
    '  <resheader name="version"><value>2.0</value></resheader>',
    '  <resheader name="reader"><value>System.Resources.ResXResourceReader, System.Windows.Forms, Version=4.0.0.0, Culture=neutral, PublicKeyToken=b77a5c561934e089</value></resheader>',
    '  <resheader name="writer"><value>System.Resources.ResXResourceWriter, System.Windows.Forms, Version=4.0.0.0, Culture=neutral, PublicKeyToken=b77a5c561934e089</value></resheader>',
    ...data,
    '</root>',
    '',
  ].join("\n");

  const folderPath = path.join(root, folder);
  fs.mkdirSync(folderPath, { recursive: true });
  fs.writeFileSync(path.join(folderPath, "Resources.resw"), xml, "utf8");
  console.log(`Wrote ${folder}/Resources.resw (${data.length} strings)`);
}
