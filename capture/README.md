# What we found in C:\Program Files\Mythicsoft\Agent Ransack\AgentRansack.exe

AgentRansack.exe is a program for Windows, built for x86_64 (64-bit). Its internal names were removed (stripped), so most functions will have generated names.

Evidence ev_9d9379c5a582f0ab3e10a22889118e2c0fc4576a2df5cb25857757d07af18c49.

```json
{
  "architecture": "x86_64",
  "bits": 64,
  "dotnet_arch": null,
  "entry_point": "0x1400fd8e0",
  "format": "pe",
  "format_name": "PE (Windows)",
  "kind": "executable",
  "libraries": [
    "ADVAPI32.dll",
    "BCGCBPRO3360u143.dll",
    "COMCTL32.dll",
    "COMDLG32.dll",
    "ConfigLib.dll",
    "ConfigUILib.dll",
    "CrashRpt1403.dll",
    "DirTraverseLib.dll",
    "GDI32.dll",
    "KERNEL32.dll",
    "MPR.dll",
    "MSVCP140.dll",
    "OLEAUT32.dll",
    "SHELL32.dll",
    "SHLWAPI.dll",
    "SearchLib.dll",
    "StreamLib.dll",
    "TextFuncLib.dll",
    "UIExtLib.dll",
    "USER32.dll",
    "UxTheme.dll",
    "VCRUNTIME140.dll",
    "VCRUNTIME140_1.dll",
    "VERSION.dll",
    "WINMM.dll",
    "api-ms-win-crt-convert-l1-1-0.dll",
    "api-ms-win-crt-heap-l1-1-0.dll",
    "api-ms-win-crt-locale-l1-1-0.dll",
    "api-ms-win-crt-math-l1-1-0.dll",
    "api-ms-win-crt-runtime-l1-1-0.dll",
    "api-ms-win-crt-stdio-l1-1-0.dll",
    "api-ms-win-crt-string-l1-1-0.dll"
  ],
  "next_steps": [
    {
      "tool": "core_overview_show",
      "why": "a one-page summary: sections, imports, counts"
    },
    {
      "tool": "native_functions_list",
      "why": "every function we found"
    },
    {
      "tool": "native_strings_find",
      "why": "find text the program shows or uses"
    },
    {
      "tool": "native_function_study",
      "why": "a full dossier on one function (start with main or the entry point)"
    },
    {
      "tool": "core_feature_find",
      "why": "follow a word you see in the app to the code behind it"
    },
    {
      "tool": "native_pe_resources_list",
      "why": "icons, version information and the manifest"
    }
  ],
  "operating_system": "Windows",
  "plain_english": "AgentRansack.exe is a program for Windows, built for x86_64 (64-bit). Its internal names were removed (stripped), so most functions will have generated names.",
  "recommended_toolsets": [
    "core",
    "native",
    "formats"
  ],
  "stripped": true,
  "symbol_table_stripped": true,
  "toolchain_hints": [
    {
      "category": "compiler",
      "evidence": "Rich header",
      "id": "msvc",
      "label": "Microsoft Visual C++ toolchain",
      "strength": "strong"
    }
  ]
}
```

## Ghidra

Deep mode: Ghidra has analysed C:\Program Files\Mythicsoft\Agent Ransack\AgentRansack.exe (1 min), so decompiling it is quick.
