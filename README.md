# Dreamy Editor Tools

Package thuộc Dreamy Game Studio. Hướng dẫn dưới đây mô tả cấu trúc, cách cài vào project và tích hợp ở root/scene.

## Cài package

Dùng Unity 6000.0 trở lên. Sandbox đã tham chiếu package bằng `file:../LocalPackages/com.dreamy.editor-tools`. Project khác dùng Package Manager > + > Install package from disk và chọn package.json, hoặc Git URL của repository nội bộ. Cài cả dependency Dreamy/Git vào manifest của game; version dependency không tự cấu hình registry riêng.

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
