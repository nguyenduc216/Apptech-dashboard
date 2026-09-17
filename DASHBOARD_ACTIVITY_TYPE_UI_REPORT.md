# Dashboard Activity Type UI Report

## Scope

- Updated `Views/Home/Index.cshtml` and `wwwroot/css/site.css`.
- Preserved the existing employee grouping, images, timestamps, request-code links, purchase detail actions, scrolling, and responsive layout.

## Classification

The UI uses the existing structured `AttendanceType` value; no text matching is used:

| AttendanceType | Display type | Color | Icon | Tooltip |
| --- | --- | --- | --- | --- |
| `ChamCong` | Normal attendance | Mint green | `fa-user-check` | `Chấm công` |
| `MuaHang` | Purchase check-in | Light blue | `fa-cart-shopping` | `Đi mua hàng` |
| `KhachHang` | Construction check-in | Light construction yellow | `fa-screwdriver-wrench` | `Chấm công công trình` |

The compact type badge is rendered at the top-right of each activity entry in both the initial Razor output and the AJAX refresh path. The entry reserves right padding for the badge so long content and media do not overlap it.

## Verification

- `dotnet build apptech-dashboard.sln -c Release`: succeeded, 0 warnings, 0 errors.
- Static review: initial and AJAX render paths use the same type labels, icons, CSS classes, native tooltip, and ARIA label.
- Desktop/mobile visual browser testing: not run in this session because browser automation was unavailable.

## Git

- Implementation commit: `6837757ac11dc5fdcf531a037dc3b4f09d3301a6`
- Push status: pending at report creation time; updated by the final task result.
