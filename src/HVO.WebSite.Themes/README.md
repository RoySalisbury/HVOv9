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

## Provided Primitives

The HVO Dark theme now ships reusable navigation elements:

- **Badge Navigation** – use `.nav-badge-bar` with `.nav-badge` links (optionally add the `nav-badge--active` class or `aria-current="page"`) for compact primary menus. Icons can be wrapped with `.nav-badge__icon`.
- **Tab Row** – apply `.hvo-tab-row` to a flex container and `.hvo-tab-row__tab` to each link or button. Mark the active entry with `.hvo-tab-row__tab--active` or `aria-current="page"` for the accent underline.

Both patterns rely on the new CSS custom properties added to `hvo-dark.css`, keeping styling consistent across consuming applications.
