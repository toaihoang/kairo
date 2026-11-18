; Unshipped analyzer release
; https://github.com/dotnet/roslyn-analyzers/blob/main/src/Microsoft.CodeAnalysis.Analyzers/ReleaseTrackingAnalyzers.Help.md

### New Rules

Rule ID | Category | Severity | Notes
--------|----------|----------|-------
KAIRO001 | Kairo | Error | The class marked with [Job] is not a valid job
KAIRO002 | Kairo | Error | Two jobs have the same name
KAIRO003 | Kairo | Warning | A job payload type has no source-generated JSON metadata
KAIRO004 | Kairo | Error | A job class must have exactly one public constructor
KAIRO005 | Kairo | Error | A job constructor parameter is not supported
KAIRO006 | Kairo | Error | A [Job] attribute value is out of range
