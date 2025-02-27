# LethalCompany-SequenceGenerator

output files will be in `BepInEx/cache/SequenceGenerator`

convert [output].mmd into an svg with [mermaid-cli](https://github.com/mermaid-js/mermaid-cli)

example command: `mmdc --configFile .\mermaidSettings.json -i [INPUT].mmd -o [OUTPUT].svg`

suggested mermaid settings:
```json
{
    "maxTextSize" : 999999
}
```
might need more if the input is big. if too big will timeout.

as of 27-Feb-2025 mermaid has an issue where the arrows in the svg have invisible stroke.  
to fix open the output svg files and replace `stroke="none"` with `stroke="black"`
