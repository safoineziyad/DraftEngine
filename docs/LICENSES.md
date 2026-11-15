# Third-party data and licenses

## Country and territory names (CLDR)

`DraftEngine.Core/Data/countries.json` is generated from the Unicode CLDR JSON
(`cldr-localenames-full`, locales `en`, `fr`, `ar`, `es`, territories):

- Source: https://github.com/unicode-org/cldr-json (package `cldr-localenames-full`)
- Upstream project: Unicode CLDR — https://cldr.unicode.org
- License: Unicode License v3 — https://www.unicode.org/license.txt
- Bundled: 264 country/territory entries with alpha-2 code and localized names.
- Runtime: embedded resource; no network request is made.

The file is generated at development time; it is not downloaded by the application.

## Subdivision lists

`DraftEngine.Core/Data/SubdivisionCatalog.cs` contains the US states and DC, Canadian
provinces and territories, Australian states and territories, and the four UK nations.
These are public factual lists, written out by hand, with no third-party data license.

## NuGet packages

Package licenses are declared by their respective authors in the NuGet package metadata.
No new package was added for the country data.
