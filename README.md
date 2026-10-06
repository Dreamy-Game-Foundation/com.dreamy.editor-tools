# Dreamy Editor Tools

Package thuộc Dreamy Game Studio. Hướng dẫn dưới đây mô tả cấu trúc, cách cài vào project và tích hợp ở root/scene.

## Cài package

Dùng Unity 6000.4 trở lên. Sandbox đã tham chiếu package bằng `file:../LocalPackages/com.dreamy.editor-tools`. Project khác dùng Package Manager > + > Install package from disk và chọn package.json, hoặc Git URL của repository nội bộ. Cài cả dependency Dreamy/Git vào manifest của game; version dependency không tự cấu hình registry riêng.

Dependency trực tiếp theo package.json:

- `com.unity.nuget.newtonsoft-json` (3.2.1)

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
