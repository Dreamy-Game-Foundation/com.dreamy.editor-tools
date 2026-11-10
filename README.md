# Dreamy Editor Tools

Package thuộc Dreamy Game Studio. Hướng dẫn dưới đây mô tả cấu trúc, cách cài vào project và tích hợp ở root/scene.

## Cài package

Phiên bản chuẩn bị phát hành: **1.2.0**. Bổ sung Package Manager với catalog mở rộng, kiểm tra phiên bản và cập nhật có preview/khôi phục; bao gồm Game View Screenshot và shortcut Clear Save Data từ 1.1.0. Chưa tạo/push tag `v1.2.0`. Package yêu cầu Unity 6000.4 để khớp Main Toolbar API đang dùng; các tag cũ được giữ nguyên.

Dùng Unity 6000.4 trở lên. Sandbox đã tham chiếu package bằng `file:../LocalPackages/com.dreamy.editor-tools`. Project khác dùng Package Manager > + > Install package from disk và chọn package.json, hoặc Git URL của repository nội bộ. Cài cả dependency Dreamy/Git vào manifest của game; version dependency không tự cấu hình registry riêng.

Dependency trực tiếp theo package.json:

- `com.unity.nuget.newtonsoft-json` (3.2.1)
- `com.unity.modules.screencapture` (1.0.0)
- `com.unity.modules.imageconversion` (1.0.0)

## Cấu trúc và asmdef

| Assembly | Reference | Phạm vi |
| --- | --- | --- |
| `Dreamy.EditorTools.Editor` | Unity.Newtonsoft.Json | Chỉ Editor |

Trong asmdef của game, thêm assembly chứa API trực tiếp sử dụng. Code bootstrap reference thêm Core/DataConfig/Datasave/Economy theo nhu cầu; code async reference UniTask. Code gọi type sample reference assembly sample. Giữ Editor reference trong asmdef Editor-only.

## Cấu trúc và sử dụng

Package chỉ có Editor; không cài service trong GameInstaller, không thêm component vào scene và không reference từ Runtime. Nếu mở rộng bằng code Editor, dùng Dreamy.EditorTools.Editor từ asmdef giới hạn Editor.

- Tools/Dreamy/Scene/Scene Manager: quản lý scene trong Build Settings, thứ tự bootstrap và scene thiếu. Toolbar hỗ trợ chọn/reload scene, Play From Bootstrap và Time Scale.
- Tools/Dreamy/Package/Package Manager: xem dependency manifest, thêm package/Git URL, resolve và mở manifest/lock.
- Tools/Dreamy/Build/Build Manager: version, Android version code, iOS build number, target/output và tùy chọn development/profiler; validate scene rồi Build hoặc Build & Run.
- Tools/Dreamy/Data Debugger: kiểm tra JSON config và save.
- Tools/Dreamy/Project: mở manifest hoặc clear console; PlayerPrefs/Clear All phục vụ reset local.

Thiết lập riêng của Editor được lưu theo project. Menu xử lý save nằm ở Datasave. Xem Edit > Shortcuts > Dreamy để đổi phím; F5 compile, Ctrl/Cmd+L khóa Inspector, Alt+PageUp/PageDown chuyển scene, Alt+R reload scene.
Phím tắt **Ctrl+Shift+Alt+C** (macOS: **Cmd+Shift+Option+C**) gọi `Tools/Dreamy/Save/Clear Save Data`, giữ hộp thoại xác nhận trước khi xóa. Cần cài Dreamy Datasave; có thể đổi tổ hợp trong **Edit > Shortcuts > Dreamy/Clear Save Data**.

### Dreamy Package Manager

Mở **Tools > Dreamy > Package > Package Manager**:

- **Dreamy Catalog** liệt kê các package chính thức và package Dreamy đã cài nhưng chưa có catalog. Xem installed version resolved, nguồn Git/Registry/Local/Embedded, direct/transitive, latest stable hoặc prerelease, lỗi và thời điểm kiểm tra. **Check Updates** đọc tag và kiểm tra `package.json` tại commit; không dùng version local làm latest.
- **Details** có chọn version, repository/tags/changelog, dependency, minimum Unity và **Preview Install / Preview Selected Version**. Preview kiểm tra dependency trước khi Apply; Git pin commit cụ thể. Dependency Dreamy thiếu được tìm qua catalog; dependency bên thứ ba thiếu mapping phải bổ sung catalog hoặc cài trước. Không hỗ trợ tùy ý các biểu thức version range ngoài minimum SemVer của UPM.
- Package local/embedded mặc định có **Open Folder**. **Switch Source to Catalog…** là thao tác riêng có preview; tool không pull/checkout submodule. Cập nhật dependency không tự chuyển nguồn local. Preview có thể thêm direct dependency để pin version đang là transitive.
- **Project Packages** giữ Add bằng ID/Git URL, Remove, Resolve, mở manifest/lock. Remove bị chặn khi còn package phụ thuộc. Add thủ công dùng trực tiếp UPM; dùng catalog để có dependency preview.
- Thao tác UPM được điều phối ngoài window và đối chiếu lại sau domain reload, không tự gửi lại request. Sau hoàn thành kiểm tra Console để xác nhận compile. **Restore Last Package Change** chỉ khả dụng khi manifest/lock còn khớp snapshot sau thao tác; không khôi phục các asset/project file khác. Restore không áp dụng cho Add thủ công không có plan.
- **Export** ghi báo cáo JSON theo vị trí được chọn. Cache metadata và operation snapshot nằm trong `Library/DreamyPackageManager/`; không cần commit.

#### Bổ sung package về sau

Catalog được nạp theo thứ tự **bundled → remote → project overrides**; entry sau có cùng ID ghi đè entry trước. UI không hardcode danh sách package.

1. Mở **Catalog Settings**, nhập Package ID, Source (GitHub Git hoặc registry UPM đã cấu hình), Repository URL, UPM subfolder nếu có, Category; bấm **Add / Override Project Entry**.
2. Entry được lưu vào `ProjectSettings/DreamyPackageCatalog.json`, có thể commit cùng project. **Open Project Catalog** cho phép sửa displayName, mô tả, documentation hoặc xóa override; **Reload Catalog** áp dụng lại.
3. Muốn mọi consumer có package mới: cập nhật `Editor/Package/DreamyPackageCatalog.json` trong editor-tools và phát hành bản package; hoặc cung cấp JSON schema tương tự qua URL HTTPS trong **Remote catalog URL** rồi **Fetch Catalog**. Lỗi mạng/schema giữ bản catalog hợp lệ trước đó. **Clear Remote** bỏ catalog remote đã cache. URL remote được nhớ riêng trên máy; cần Fetch lại khi muốn danh sách mới.

Ví dụ catalog project/remote:

```json
{
  "schemaVersion": 1,
  "packages": [
    {
      "id": "com.dreamy.new-feature",
      "displayName": "Dreamy New Feature",
      "category": "Feature",
      "source": "git",
      "repository": "https://github.com/Dreamy-Game-Foundation/com.dreamy.new-feature",
      "packagePath": "",
      "description": "Short package description"
    }
  ]
}
```

Git release discovery hiện hỗ trợ GitHub HTTPS, SemVer tags (`v1.2.3` hoặc `1.2.3`) và metadata khớp package ID/version. Repo monorepo dùng `packagePath`. Registry entry đặt `source: "registry"` và sử dụng registry đã cấu hình trong Unity. Tool không tự cấu hình scoped registry. Không tự khám phá mọi repo trong GitHub org: release owner thêm package vào catalog để xác định package chính thức.

Repo private/rate limit: cấu hình `DREAMY_GITHUB_TOKEN` (hoặc `GH_TOKEN`/`GITHUB_TOKEN`) trong môi trường **trước khi mở Unity**; token chỉ gửi đến `api.github.com`, không ghi vào catalog/preferences/cache. Git install dùng credential Git của máy do UPM quản lý, độc lập với token tra metadata. Tool không yêu cầu GitHub CLI. Các tag không khớp metadata bị đánh dấu invalid; không xem lỗi mạng/auth là Up to date. Dữ liệu cache luôn ghi thời điểm kiểm tra; refresh thất bại giữ version cũ kèm lỗi, chặn Apply đến khi Retry thành công.

Minimum Unity/UPM compatibility là kiểm tra metadata, không phải chứng nhận tương thích API hay gameplay. Planner chặn các dependency chưa xác định; không tự nâng hàng loạt. Update Selected/Update All và import sample chưa nằm trong bản này.

## Sample

Manifest hiện không khai báo sample để import qua Package Manager.

## Addressables

Package này không có panel cần đăng ký vào Addressables Group. Việc đặt address của prefab/asset thuộc game hoặc package UI/Assets; không dùng Addressables thay bước đăng ký service/config/save.

## Data Debugger và toolbar

- Visual hiển thị object thành các dòng field, list thành bảng. Bấm phần preview hoặc mũi tên **▸/▾** trong ô để mở/thu gọn object/list **ngay bên dưới hàng chứa ô đó**, giữ nguyên bảng cha. Mỗi khối con có tên field, số phần tử, nền riêng và đường thụt lề; nút **▾** ở góc phải thu gọn khối. Nhiều ô có thể mở cùng lúc.
- List con hiển thị tối đa 12 phần tử mỗi trang, có **< / >**, **+ Row** và nút **×** xóa từng item. Item có hơn 4 field dùng bố cục field/value dọc để tránh cột quá hẹp. Chuột phải hàng con dùng menu copy/paste, duplicate, insert, move và delete như bảng cha.
- Chuỗi bắt đầu bằng `{` hoặc `[` có nút mũi tên để sửa JSON bên trong ngay dưới ô. Khi sửa, dữ liệu vẫn được lưu dưới dạng chuỗi JSON; envelope `Payload` và metadata của save được giữ nguyên.
- **Ctrl/Cmd+S** lưu file đang chọn. Phím copy/paste/delete của bảng chỉ hoạt động khi không nhập text. Khi đóng cửa sổ có dữ liệu chưa lưu, Unity hỏi Save/Discard/Cancel.
- Trong Datasave, **Reset Save** hoặc menu chuột phải **Reset Save (no backup)** xóa file được chọn cùng `.bak`, `.tmp` và `.bak-*` của đúng save đó, không tạo backup mới. **Reset Saves** thực hiện tương tự cho tất cả save đang liệt kê. Save chỉ còn backup được đánh dấu **(backup only)** và vẫn reset được.
- **Backup on Save** chỉ tạo bản backup Editor trước khi lưu chỉnh sửa. Runtime Datasave có backup `.bak` riêng để phục hồi save; checkbox này không tắt backup runtime. Reset chỉ chạy khi đã dừng Play Mode để dữ liệu còn trong bộ nhớ không được game ghi trở lại. Để test dữ liệu mới: dừng Play → Reset Save → chạy Play lại. Backup khi game lưu bình thường, bao gồm production, vẫn giữ nguyên.
- Save bị hủy nếu file trên đĩa đã đổi hoặc bị xóa sau khi load. Chuyển sang Text và copy JSON đang sửa trước khi Reload để tránh mất công chỉnh sửa.
- Bảng cache danh sách cột/kết quả lọc và chỉ vẽ các hàng trong vùng nhìn thấy. Có thể kéo cột, lọc, sắp xếp, thêm/nhân bản/xóa hàng, copy TSV và xuất CSV.
- Main Toolbar: bật nhóm **Dreamy/Audio and Scene View** trong cấu hình toolbar nếu đang bị ẩn. **Mute** tắt/bật âm thanh Editor; **2D** chuyển Scene View hoạt động gần nhất giữa 2D/3D, giống nút 2D của Scene View. Các nút đồng bộ khi thay đổi từ điều khiển Unity.

Main Toolbar API hiện dùng trong package yêu cầu Unity 6000.4; sandbox được kiểm tra với 6000.4.12f1.

Kiểm tra logic dữ liệu (không thay thế kiểm tra GUI trong Unity): chạy `python3 LocalPackages/com.dreamy.editor-tools/Tests~/validate-data-debugger.py` từ sandbox; cần .NET SDK 10 và Newtonsoft.Json đã được Unity tải vào Library/PackageCache.

## Game View Screenshot (1.1.0)

Mở **Tools > Dreamy > Capture > Game View Screenshot**. Vào Play Mode, để Game View tiếp tục render, chọn chế độ rồi bấm **Capture Game View**:

| Chế độ | Kết quả |
| --- | --- |
| **Copy** (mặc định) | Copy ảnh vào clipboard để paste; không tạo file ảnh/thư mục/temp |
| **Save** | Lưu PNG vào thư mục đã chọn; không thay đổi clipboard |
| **Copy + Save** | Cùng một frame được copy và lưu; báo kết quả từng thao tác nếu lỗi |

Có countdown 0/3/5 giây, Cancel, preview Fit/100%, **Copy Image** và **Save As…** từ ảnh vừa chụp. Save As chọn thư mục rồi tạo tên PNG duy nhất. **Copy Path**, **Reveal** và **Open** chỉ bật khi có file đã lưu. Copy Path thay clipboard thành text, vì vậy không chạy tự động sau Copy Image.

Menu **Capture Game View** và shortcut **Dreamy/Capture Game View** dùng lại chế độ/countdown/thư mục đã chọn. Shortcut mặc định chưa gán, có thể cấu hình trong **Edit > Shortcuts**. **Open Capture Folder** chỉ mở thư mục đã tồn tại.

Ảnh lấy trực tiếp Game View ở độ phân giải hiện tại, gồm UI đã render. Không chụp desktop/Scene View, không tự đổi aspect/resolution và không tự Play/unpause. Bản đầu chưa hỗ trợ Edit Mode, Editor pause hoặc batch/headless capture. Khi thiếu frame sẽ timeout sau 10 giây; countdown dùng thời gian thực, không phụ thuộc timeScale.

### Clipboard Windows và Ubuntu

- Windows: dùng clipboard ảnh Win32 (CF_DIB); không cần tool hệ thống ngoài Unity. Native paste trên Windows vẫn cần receipt từ Windows host; compile Linux không chứng minh clipboard Windows hoạt động.
- Ubuntu Wayland: cần executable **wl-copy** từ package `wl-clipboard` trong PATH của Unity. Có thể cài bằng `sudo apt install wl-clipboard`, sau đó mở lại Unity để nhận PATH.
- Ubuntu X11: cần **xclip** (`sudo apt install xclip`). Session Wayland ưu tiên wl-copy, không tự chuyển sang clipboard X11.
- Trong cửa sổ có **Clipboard requirements**: hiển thị yêu cầu theo Windows/Ubuntu Wayland/X11. Khi thiếu helper, phần này luôn hiện, kể cả trước Play Mode. Lệnh cài đặt có thể chọn text hoặc bấm **Copy Install Command** rồi paste vào Terminal; nút chỉ copy lệnh, không tự chạy/cài đặt. Sau khi cài, mở lại Unity.
- Thiếu helper: UI giải thích và cho phép chọn **Save**. Package không tự cài tool và không tự chuyển Copy thành Save.
- Helper nhận PNG qua stdin; không tạo file ảnh tạm. Clipboard owner có thể sống sau khi đóng cửa sổ screenshot; thời gian giữ nội dung phụ thuộc desktop/clipboard manager. Không cam kết mọi app đều nhận paste ảnh.

Output mặc định `<project>/Screenshots/`; nên thêm thư mục này vào `.gitignore` của consumer. File có tên UTC + ID duy nhất, không ghi đè. Preferences lưu riêng theo project ở `UserSettings/DreamyCapture/Preferences.asset`; Copy-only vẫn có thể lưu preferences. Công cụ không kiểm soát chính sách lưu lịch sử của clipboard manager hệ điều hành.

Preview chỉ giữ ảnh cuối, giải phóng khi đóng cửa sổ hoặc reload. Đóng cửa sổ hủy capture do cửa sổ khởi tạo; capture từ menu/shortcut có owner độc lập. Khi output đã copy/lưu thành công, Cancel không rollback kết quả đó. Giới hạn ảnh là 32 megapixel, đồng thời kiểm tra dự toán bộ nhớ tạm 512 MiB (Windows có thể bị giới hạn sớm hơn do bản sao DIB). Đây là dự toán bảo vệ, không phải peak memory đã đo. Ảnh lớn có thể gây hitch khi readback/encode.

### Kiểm tra

Test assembly **Dreamy.EditorTools.Tests.Editor** chỉ dành cho Editor. Thêm `com.dreamy.editor-tools` vào `testables` của manifest consumer để chạy nhóm **Dreamy.EditorTools.Tests**. Tests kiểm tra các chế độ, hủy, lỗi từng đích, không ghi đè và batch guard; paste/capture thật cần GUI host tương ứng.
