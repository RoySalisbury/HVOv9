# HVO.WebSite.Themes

Shared Razor Class Library for front-end styling assets used across HVO web sites. The initial release packages the **HVO Dark** design system so any site can reference it via static web assets.

## Usage

1. Reference the project (or NuGet package when published) from your web app:
   ```xml
   <ProjectReference Include="../HVO.WebSite.Themes/HVO.WebSite.Themes.csproj" />
   ```
2. In your layout or root component add the shared stylesheet:
   ```html
   <link rel="stylesheet" href="_content/HVO.WebSite.Themes/css/themes/hvo-dark.css" />
   ```
3. Ensure `<html>` and `<body>` include `data-theme="hvo-dark"` so the CSS variables apply.

## Adding Assets

- Place shared styles under `wwwroot/css/` and fonts under `wwwroot/fonts/`.
- Keep tokens (CSS custom properties) in `css/themes/hvo-dark.css` to avoid duplication.
- Add component- or brand-specific overrides to the consuming site, not here.
