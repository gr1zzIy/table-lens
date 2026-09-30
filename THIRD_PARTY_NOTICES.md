# Third-party notices

TableLens depends on the following separately licensed projects. This list names direct dependencies and the bundled font; NuGet also restores their transitive dependencies. It does not relicense those components.

| Component | License / source |
| --- | --- |
| Avalonia, Fluent theme and platform integrations | [MIT](https://github.com/AvaloniaUI/Avalonia/blob/master/licence.md) |
| Avalonia DataGrid | [MIT](https://github.com/AvaloniaUI/Avalonia.Controls.DataGrid/blob/master/LICENSE) |
| Avalonia Fonts.Inter / Inter font | [SIL Open Font License 1.1](https://github.com/rsms/inter/blob/master/LICENSE.txt) |
| CsvHelper | [MS-PL or Apache-2.0](https://github.com/JoshClose/CsvHelper) |
| DotNetDBF | [LGPL-2.1-or-later; upstream source](https://github.com/ekonbenefits/dotnetdbf) |
| SkiaSharp native rendering dependency | [MIT and third-party notices](https://github.com/mono/SkiaSharp) |
| xUnit / Microsoft test SDK / Avalonia Headless | Test dependencies; their packages retain their own licenses |

DotNetDBF is consumed as an unmodified separate NuGet assembly and remains a separate DLL in published folders. Keep its license and upstream source reference when distributing binary packages. NuGet package metadata and upstream license files are the authoritative terms.
