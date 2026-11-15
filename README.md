# Hệ thống quản lý học viên nghỉ phép

Ứng dụng web nội bộ của Học viện An ninh nhân dân dùng để lập, duyệt, lưu trữ và tra cứu đơn nghỉ phép của học viên. Hệ thống xây dựng trên ASP.NET Core Blazor Server (.NET 8), ASP.NET Core Identity và SQLite. Toàn bộ dữ liệu nằm trong thư mục `QuanLyNghiPhep/AppData` của máy chủ.

## Mục lục

1. [Bối cảnh và mục tiêu](#1-bối-cảnh-và-mục-tiêu)
2. [Vai trò và phạm vi chức năng](#2-vai-trò-và-phạm-vi-chức-năng)
3. [Quy trình xử lý đơn nghỉ](#3-quy-trình-xử-lý-đơn-nghỉ)
4. [Kiến trúc kỹ thuật](#4-kiến-trúc-kỹ-thuật)
5. [Mô hình dữ liệu và quy tắc tính toán](#5-mô-hình-dữ-liệu-và-quy-tắc-tính-toán)
6. [Cài đặt và khởi chạy](#6-cài-đặt-và-khởi-chạy)
7. [Tài khoản và bảo mật](#7-tài-khoản-và-bảo-mật)
8. [Cấu hình](#8-cấu-hình)
9. [Nhập danh sách lớp](#9-nhập-danh-sách-lớp)
10. [Lưu trữ, sao lưu và nâng cấp](#10-lưu-trữ-sao-lưu-và-nâng-cấp)
11. [Giao diện](#11-giao-diện)
12. [Kiểm thử](#12-kiểm-thử)
13. [Cấu trúc thư mục](#13-cấu-trúc-thư-mục)
14. [Giới hạn hiện tại](#14-giới-hạn-hiện-tại)

---

## 1. Bối cảnh và mục tiêu

**Vấn đề.** Mỗi tuần, cán bộ chủ nhiệm trung đội lập danh sách học viên nghỉ cuối tuần, nghỉ lễ Tết và nghỉ tranh thủ. Phòng Quản lý học viên (Phòng QLHV) duyệt và lưu trữ các danh sách này. Giáo viên bộ môn cần biết học viên nào vắng mặt trong môn mình phụ trách. Khi dữ liệu nằm trong các tệp Excel rời rạc, ba nhóm người dùng không có chung một nguồn dữ liệu, và không ai kiểm soát được ai đã sửa đơn nào.

**Khoảng trống.** Tệp Excel dùng chung không phân quyền theo lớp. Một cán bộ trung đội có thể sửa danh sách của lớp khác. Tệp cũng không ghi lại người duyệt và thời điểm duyệt. Khi thông tin học viên thay đổi, các đơn đã duyệt trước đó bị thay đổi theo, nên hồ sơ lịch sử mất giá trị đối chiếu.

**Yêu cầu.** Hệ thống phải đáp ứng bốn tiêu chí sau.

- Kiểm tra quyền ở phía máy chủ cho mọi thao tác đọc, thêm, sửa, xoá, duyệt và xuất dữ liệu.
- Lưu bản chụp thông tin học viên và lớp tại thời điểm lập đơn.
- Ghi nhật ký người thực hiện và thời điểm cho các thao tác nghiệp vụ.
- Chạy được trên một máy chủ đơn lẻ, không cần máy chủ cơ sở dữ liệu riêng.

**Ý tưởng cốt lõi.** Mỗi đơn nghỉ là một bản ghi có trạng thái. Chỉ vai trò được giao mới chuyển được đơn từ trạng thái này sang trạng thái kế tiếp. Quyền gắn với ID tài khoản quản lý lớp (`ManagerUserId`) chứ không gắn với tên đăng nhập, nên việc đổi tên tài khoản không làm mất quyền. Hai tệp SQLite tách riêng dữ liệu tài khoản và dữ liệu nghiệp vụ, giúp sao lưu và khôi phục mà không cần cài thêm phần mềm.

## 2. Vai trò và phạm vi chức năng

Hệ thống có bốn vai trò. Mỗi tài khoản giữ đúng một vai trò.

| Vai trò (mã kỹ thuật) | Phạm vi dữ liệu | Chức năng chính |
|---|---|---|
| Quản trị viên (`Admin`) | Toàn bộ tài khoản | Tạo tài khoản cá nhân hoặc tài khoản khoa. Tìm kiếm, lọc, khoá, mở khoá, bỏ khoá tạm. Đổi tên và đặt lại mật khẩu. Xoá tài khoản chưa có liên kết hoặc lịch sử. Xuất Excel danh sách tài khoản vừa cấp. Xem nhật ký hệ thống. Sao lưu và khôi phục dữ liệu. |
| Phòng Quản lý học viên (`PhongQLHV`) | Toàn bộ lớp và đơn đã qua trung đội | Tạo lớp bằng Excel, CSV hoặc nhập tay. Phân công tài khoản trung đội cho lớp. Đổi tên lớp hoặc xoá lớp chưa có đơn. Thêm, sửa, ngừng theo dõi, khôi phục hoặc xoá hồ sơ học viên chưa sử dụng. Duyệt hoặc từ chối đơn chờ. Huỷ đơn kèm lý do. Quản lý danh mục lễ Tết và danh mục trung đội. Xem thống kê, tra cứu và xuất Excel. |
| Chủ nhiệm trung đội (`TrungDoi`) | Các lớp được phân công | Thêm, sửa, xoá môn học của lớp và gắn khoa phụ trách. Lập danh sách nghỉ cuối tuần, nghỉ lễ Tết và nghỉ tranh thủ. Huỷ đơn kèm lý do. Xuất Excel dữ liệu của lớp. |
| Giáo viên bộ môn (`BoMon`) | Môn được phân công | Xem đơn nghỉ tranh thủ đã duyệt thuộc các môn mình phụ trách. Đơn duyệt trong ba ngày gần nhất được đánh dấu "Mới". |

Mọi vai trò đều tự đổi được mật khẩu. Sau khi đổi, phiên hiện tại kết thúc và người dùng phải đăng nhập lại.

## 3. Quy trình xử lý đơn nghỉ

### 3.1. Luồng nghiệp vụ tổng quát

Phòng QLHV tạo lớp và phân công tài khoản trung đội. Cán bộ trung đội nhập môn học và khoa phụ trách cho lớp, sau đó lập danh sách nghỉ. Phòng QLHV duyệt đơn và lưu trữ. Giáo viên bộ môn tra cứu đơn nghỉ tranh thủ liên quan đến môn của mình. Mỗi đơn tranh thủ gắn với đúng một môn thuộc lớp và một khoa phụ trách.

### 3.2. Trạng thái của đơn

Một đơn nghỉ đi qua các trạng thái sau.

| Trạng thái | Phát sinh khi | Vai trò được chuyển trạng thái tiếp |
|---|---|---|
| Chờ duyệt trung đội | Đơn được tạo theo luồng hai bước bằng biểu mẫu từng đơn | Chủ nhiệm trung đội |
| Chờ duyệt Phòng QLHV | Trung đội lập danh sách hoặc duyệt đơn ở bước trước | Phòng QLHV |
| Đã duyệt | Phòng QLHV duyệt đơn | Trung đội hoặc Phòng QLHV có thể huỷ |
| Từ chối | Trung đội hoặc Phòng QLHV từ chối | Trạng thái kết thúc |
| Đã huỷ | Trung đội hoặc Phòng QLHV huỷ đơn kèm lý do | Trạng thái kết thúc |

Thao tác huỷ chỉ áp dụng cho đơn ở trạng thái Chờ duyệt Phòng QLHV hoặc Đã duyệt. Đơn bị huỷ vẫn được giữ trong cơ sở dữ liệu, và lý do huỷ được ghi vào nhật ký. Phòng QLHV có thể duyệt cùng lúc mọi đơn đang chờ của một lớp theo một loại nghỉ.

### 3.3. Định mức nghỉ tranh thủ

Tham số `Leave:TranhThuMonthlyLimit` quy định số đơn nghỉ tranh thủ tối đa của một học viên trong một tháng. Khi vượt định mức, giao diện hiển thị cảnh báo nhưng không chặn việc lập đơn. Quyết định cuối cùng vẫn thuộc người duyệt.

## 4. Kiến trúc kỹ thuật

### 4.1. Thành phần

| Thành phần | Công nghệ | Vai trò |
|---|---|---|
| Giao diện | Blazor Server, render mode `InteractiveServer` | Toàn bộ màn hình nghiệp vụ chạy trên một trang (`/`). Tab đang mở được lưu trên query string (`/?tab=...`). |
| Xác thực | ASP.NET Core Identity, cookie `LeaveManagement.Auth` | Đăng nhập, khoá tài khoản, đổi mật khẩu. Các endpoint `/account/login`, `/account/logout`, `/account/renew`, `/account/change-password` là Minimal API, dùng form POST có kiểm tra antiforgery. |
| Dữ liệu tài khoản | SQLite qua EF Core (`accounts.db`) | Lưu người dùng, vai trò, hồ sơ cá nhân và hash mật khẩu. |
| Dữ liệu nghiệp vụ | SQLite qua EF Core (`business.db`) | Lưu lớp, học viên, môn học, đơn nghỉ, danh mục và nhật ký. |
| Nghiệp vụ | `LeaveRepository` (singleton) | Kiểm tra quyền và quy tắc nghiệp vụ, rồi ghi dữ liệu trong một transaction. |
| Sao lưu | `BackupService`, `DailyBackupService` | Tạo bản sao lưu thủ công hoặc tự động, và khôi phục khi khởi động. |
| Xuất nhập Excel | `ExcelExporter` | Đọc tệp `.xlsx` và CSV khi tạo lớp, xuất báo cáo `.xlsx`. |

### 4.2. Tính nhất quán khi ghi dữ liệu

Nhiều phiên Blazor có thể cùng gọi `LeaveRepository`. Để tránh hai phiên ghi đè dữ liệu của nhau, các instance repository trong cùng tiến trình dùng chung một khoá. Trước mỗi thao tác, repository nạp lại trạng thái mới nhất từ SQLite. Thao tác ghi chạy trong transaction. Nếu ghi thất bại, trạng thái trong bộ nhớ không được cập nhật, và bộ đếm mã đơn không tăng.

Cơ chế này chỉ có hiệu lực trong một tiến trình. Do đó, mỗi cặp `accounts.db` và `business.db` chỉ được phục vụ bởi một tiến trình máy chủ. Không chỉnh sửa database bằng công cụ bên ngoài khi ứng dụng đang chạy.

## 5. Mô hình dữ liệu và quy tắc tính toán

### 5.1. Các thực thể chính

| Thực thể | Khoá | Trường quan trọng |
|---|---|---|
| `ApplicationUser` | ID Identity | Tên đăng nhập, hash mật khẩu, vai trò, trạng thái. Tài khoản cá nhân có họ tên, ngày sinh, giới tính, số điện thoại, số hiệu CAND. Tài khoản khoa có tên khoa. |
| `ClassGroup` | `TenLop` | `ManagerUserId` (ID tài khoản quản lý), trung đội, người tạo, ngày tạo. |
| `Student` | `MaHocVien` | STT, họ tên, lớp, ngày sinh, số điện thoại, trung đội, `IsActive`. |
| `ClassSubject` | `Id` | Lớp, mã môn, tên môn, khoa, ID tài khoản khoa. Mã môn là duy nhất trong một lớp. |
| `LeaveRequest` | `Id`, `MaDon` | Mã học viên, bản chụp họ tên, lớp và trung đội, thời điểm bắt đầu và kết thúc, lý do, địa chỉ, loại nghỉ, môn học, trạng thái, người lập, người duyệt và thời điểm tương ứng. |
| `AuditEntry` | `Id` | Thời điểm, ID tài khoản thực hiện, hành động, chi tiết. |

### 5.2. Quy tắc tính toán

Các đại lượng dưới đây được tính từ hai thời điểm bắt đầu $t_0$ và kết thúc $t_1$ của đơn.

| Đại lượng | Công thức | Ghi chú |
|---|---|---|
| Loại đơn | Trong ngày nếu $t_0$ và $t_1$ cùng ngày, ngược lại là Dài ngày | |
| Số giờ | $\max(0,\ t_1 - t_0)$ tính bằng giờ | Giá trị âm do nhập sai được đưa về 0. |
| Số ngày quy đổi | Số giờ chia 24, làm tròn hai chữ số thập phân | Đây là thời lượng quy đổi, không phải số ngày học. |
| Lượt nghỉ của học viên | Số đơn có cùng `MaHocVien` | Không so khớp theo họ tên vì có thể trùng tên. |

**Kiểm tra trùng khoảng nghỉ.** Đơn mới $[a_1, b_1)$ trùng với đơn cũ $[a_0, b_0)$ khi $a_1 < b_0$ và $b_1 > a_0$. Hai khoảng tiếp giáp nhau ($b_0 = a_1$) không bị coi là trùng. Đơn đã bị từ chối hoặc đã huỷ không chặn việc lập đơn mới.

**Báo cáo theo tháng.** Bộ lọc tháng $m$ chọn các đơn có khoảng thời gian giao với $[\text{đầu tháng } m,\ \text{đầu tháng } m+1)$. Một đơn kéo dài từ cuối tháng này sang đầu tháng sau xuất hiện trong báo cáo của cả hai tháng.

### 5.3. Bảo toàn lịch sử

- Đơn đã xử lý giữ nguyên bản chụp thông tin học viên và lớp. Khi sửa hồ sơ học viên, hệ thống chỉ đồng bộ sang các đơn còn ở trạng thái Chờ duyệt trung đội.
- Không đổi được mã học viên đã có lịch sử. Không chuyển được học viên sang lớp khác khi học viên còn đơn đang chờ xử lý.
- Học viên có hồ sơ liên kết được chuyển sang trạng thái ngừng theo dõi thay vì xoá hẳn, và có thể khôi phục. Học viên ngừng theo dõi không xuất hiện khi lập đơn mới.
- Lớp có tên người quản lý cũ không xác định được ID cần được Phòng QLHV phân công lại.

## 6. Cài đặt và khởi chạy

### 6.1. Yêu cầu

- .NET 8 SDK, hoặc Visual Studio 2022 có workload ASP.NET and web development.
- Node.js 20 trở lên, chỉ cần khi chạy kiểm thử HTTP.

### 6.2. Khởi chạy bằng script

Tệp `start.ps1` ở thư mục gốc thực hiện các bước kiểm tra trước khi chạy. Script kiểm tra .NET SDK, dừng instance `QuanLyNghiPhep` cũ để tránh khoá tệp khi build, kiểm tra cổng, tạo chứng chỉ HTTPS phát triển nếu thiếu, restore NuGet khi cần, rồi mở trình duyệt khi máy chủ sẵn sàng.

```powershell
.\start.ps1              # HTTPS tại https://localhost:7187
.\start.ps1 -Watch       # dotnet watch, tự nạp lại khi sửa code
.\start.ps1 -Http        # chỉ HTTP tại http://localhost:5187
.\start.ps1 -Release     # cấu hình Release
.\start.ps1 -NoBrowser   # không tự mở trình duyệt
.\start.ps1 -Restore     # restore NuGet trước khi chạy
```

Nếu Windows chặn chạy script, chạy lệnh sau một lần.

```powershell
Set-ExecutionPolicy -Scope CurrentUser RemoteSigned
```

### 6.3. Khởi chạy thủ công

Chạy các lệnh dưới đây từ thư mục chứa `QuanLyNghiPhep.sln`.

```powershell
dotnet restore QuanLyNghiPhep.sln
dotnet build QuanLyNghiPhep.sln -c Release
dotnet run --project QuanLyNghiPhep --launch-profile https
```

Trong Visual Studio, mở `QuanLyNghiPhep.sln` ở thư mục gốc, chọn `QuanLyNghiPhep` làm Startup Project, rồi chạy profile `https`.

## 7. Tài khoản và bảo mật

### 7.1. Tài khoản ban đầu

Ở lần chạy đầu tiên trên một database trống, ứng dụng tạo bốn tài khoản `admin`, `trungdoi`, `phongqlhv` và `bomon`. Mỗi tài khoản có mật khẩu ngẫu nhiên riêng dài 10 ký tự. Danh sách được ghi vào `QuanLyNghiPhep/AppData/tai-khoan-ban-dau.txt`. Sau khi bàn giao, người vận hành cần đổi mật khẩu và xoá tệp này.

Khi nâng cấp một database cũ chưa có tài khoản Admin, ứng dụng tạo thêm một tài khoản Admin và ghi vào `AppData/admin-ban-dau.txt`. Ứng dụng không bao giờ đặt lại mật khẩu của tài khoản đã tồn tại khi khởi động lại.

Nếu `accounts.db` trống nhưng `tai-khoan-ban-dau.txt` vẫn còn, ứng dụng dừng khởi động và báo lỗi. Tình huống này thường xuất hiện khi database bị xoá nhầm, nên người vận hành cần kiểm tra bản sao lưu trước khi khởi tạo lại.

### 7.2. Quản lý tài khoản bằng dòng lệnh

Khi không đăng nhập được vào giao diện, người vận hành quản lý tài khoản trực tiếp trên máy chủ bằng các lệnh sau.

```powershell
dotnet run --project QuanLyNghiPhep -- --account create <tên> <TrungDoi|PhongQLHV|BoMon>
dotnet run --project QuanLyNghiPhep -- --account reset-password <tên>
dotnet run --project QuanLyNghiPhep -- --account disable <tên>
dotnet run --project QuanLyNghiPhep -- --account enable <tên>
```

Lệnh `create` và `reset-password` in mật khẩu mới ra màn hình. Mật khẩu này cần được bàn giao riêng cho người dùng.

### 7.3. Các cơ chế bảo vệ

| Cơ chế | Thiết lập |
|---|---|
| Chính sách mật khẩu | Dài từ 8 đến 256 ký tự, có chữ hoa, chữ thường, chữ số, ký tự đặc biệt và ít nhất 4 ký tự khác nhau. |
| Khoá tạm khi sai mật khẩu | Sai 5 lần liên tiếp thì tài khoản bị khoá 5 phút. |
| Giới hạn tần suất | Endpoint đăng nhập và đổi mật khẩu nhận tối đa 20 yêu cầu mỗi phút cho một địa chỉ IP. Yêu cầu vượt ngưỡng nhận mã HTTP 429. |
| Thời hạn phiên | 120 phút, không tự gia hạn theo hoạt động. Khi còn 5 phút, giao diện hiện cảnh báo kèm nút gia hạn. |
| Thu hồi phiên | Đăng xuất, đổi mật khẩu hoặc thay đổi trạng thái tài khoản sẽ cập nhật security stamp. Mọi phiên cũ của tài khoản đó bị vô hiệu. |
| Chống CSRF | Mọi form POST đều kiểm tra antiforgery token. |
| Khoá mã hoá | Khoá DataProtection lưu tại `AppData/keys` với tên ứng dụng `QuanLyNghiPhepHV`. |

**Lưu ý về mật khẩu hiển thị cho Admin.** Ngoài hash dùng để xác thực, hệ thống lưu thêm một bản mật khẩu được mã hoá bằng DataProtection để Admin xem lại khi bàn giao. Bản này chỉ giải mã được khi còn đúng thư mục `AppData/keys`. Vì vậy, thư mục `AppData` phải được bảo vệ như dữ liệu chứa mật khẩu, và không được đưa lên Git hoặc chia sẻ công khai.

## 8. Cấu hình

Các tham số nghiệp vụ nằm trong mục `Leave` của `QuanLyNghiPhep/appsettings.json`.

| Khoá | Mặc định | Ý nghĩa |
|---|---|---|
| `Leave:DefaultAddress` | Phường Hà Đông, Thành phố Hà Nội | Địa chỉ điền sẵn cho đơn nghỉ cuối tuần. |
| `Leave:PlatoonPrefix` | Trung đội | Tiền tố khi hiển thị tên trung đội. |
| `Leave:TranhThuMonthlyLimit` | 3 | Số đơn nghỉ tranh thủ mỗi học viên mỗi tháng trước khi giao diện hiện cảnh báo. |

Cổng chạy được khai báo trong `QuanLyNghiPhep/Properties/launchSettings.json`. Profile `http` dùng cổng 5187, profile `https` dùng cổng 7187 và 5187.

## 9. Nhập danh sách lớp

Phòng QLHV tạo lớp bằng tệp `.xlsx` hoặc CSV/`.txt`. Mẫu tệp được tải trực tiếp trên giao diện. Bộ nhập áp dụng các ràng buộc sau.

- Tệp tối đa 10 MB và tối đa 1.000 học viên mỗi lần nhập.
- Với `.xlsx`, hệ thống đọc sheet đầu tiên theo thứ tự trong workbook, và hỗ trợ ngày lưu dưới dạng số.
- Với CSV, hệ thống nhận mã hoá UTF-8 và dấu phân cách là dấu phẩy, dấu chấm phẩy hoặc tab. Trường đặt trong ngoặc kép được phép chứa dấu phân cách và xuống dòng.
- Ngày sinh nhận định dạng ngày/tháng/năm hoặc ISO, trong khoảng từ năm 1900 đến hiện tại.
- Số điện thoại phải có 10 chữ số và bắt đầu bằng 0.
- Hệ thống không tự điền ngày sinh hoặc số điện thoại bị thiếu.
- Một dòng sai làm huỷ toàn bộ thao tác tạo lớp. Người dùng sửa lỗi trên bản nháp rồi lưu lại cả lớp.

Mã học viên nhập trùng bị từ chối. Khi để trống, mã được sinh từ số thứ tự lớn nhất hiện có trong lớp.

Khi xuất báo cáo, mọi ô được ghi dưới dạng chuỗi văn bản (inline string). Nhờ đó, dữ liệu bắt đầu bằng ký tự công thức như `=` được hiển thị nguyên văn và không bị Excel thực thi. Báo cáo của Phòng QLHV xuất toàn bộ phạm vi quyền hoặc đúng tập kết quả của bộ lọc đang áp dụng.

## 10. Lưu trữ, sao lưu và nâng cấp

### 10.1. Nội dung thư mục AppData

| Đường dẫn | Nội dung |
|---|---|
| `accounts.db` | Tài khoản, vai trò, hồ sơ và hash mật khẩu. |
| `business.db` | Lớp, học viên, môn học, đơn nghỉ, danh mục và nhật ký. |
| `keys/` | Khoá DataProtection cho cookie và mật khẩu mã hoá. |
| `backups/` | Các bản sao lưu dạng `.zip`. |
| `du-lieu-nghi-phep.json` | Dữ liệu JSON của phiên bản cũ, chỉ dùng để chuyển đổi ban đầu. |
| `tai-khoan-ban-dau.txt` | Mật khẩu ban đầu. Cần xoá sau khi bàn giao. |

Tệp JSON cũ chỉ được đọc khi chưa có `business.db`. Sau khi chuyển đổi, tệp gốc được giữ nguyên và không còn là nguồn dữ liệu hoạt động. Nếu JSON có bản ghi không hợp lệ, ứng dụng báo lỗi để người vận hành sửa nguồn, thay vì bỏ qua bản ghi đó. Dữ liệu mẫu trong code chỉ được dùng khi không có cả database nghiệp vụ lẫn tệp JSON cũ.

### 10.2. Sao lưu

Bản sao lưu được tạo bằng lệnh SQLite `VACUUM INTO` rồi nén thành tệp `.zip`. Cách này cho bản sao nhất quán ngay cả khi ứng dụng đang chạy.

- **Sao lưu thủ công.** Admin tạo và tải bản sao lưu trên giao diện. Tệp có tiền tố `sao-luu-`.
- **Sao lưu tự động.** Dịch vụ nền chạy 30 giây sau khi khởi động, sau đó kiểm tra mỗi giờ một lần. Nếu trong 23 giờ gần nhất chưa có bản tự động nào, dịch vụ tạo một bản có tiền tố `tu-dong-`. Hệ thống giữ 14 bản tự động mới nhất và xoá các bản cũ hơn.

### 10.3. Khôi phục

Admin tải bản sao lưu lên giao diện. Bản này được đặt vào thư mục chờ `restore-pending` và chỉ được áp dụng ở lần khởi động tiếp theo. Trước khi ghi đè, ứng dụng tự tạo một bản sao lưu `truoc-khoi-phuc-*.zip` của dữ liệu hiện tại để có thể quay lại.

### 10.4. Nâng cấp phiên bản

1. Dừng ứng dụng.
2. Sao lưu toàn bộ thư mục `QuanLyNghiPhep/AppData`, gồm hai database, tệp JSON và thư mục `keys`.
3. Thay mã nguồn bằng phiên bản mới.
4. Chép lại `AppData` vào đúng vị trí cũ.
5. Chạy phiên bản mới và kiểm tra danh sách lớp cùng phân công.

Không xoá `AppData` để xử lý lỗi đăng nhập, vì thao tác này xoá toàn bộ dữ liệu. Hãy dùng lệnh `--account reset-password` ở mục 7.2. Không đổi tên ứng dụng DataProtection trong `Program.cs`. Nếu đổi, các khoá cũ trong `AppData/keys` không còn giải mã được dữ liệu.

## 11. Giao diện

Sau khi đăng nhập, mỗi vai trò làm việc trên cùng một bố cục gồm ba vùng.

- **Thanh điều hướng bên trái** chạy hết chiều cao màn hình. Phần đầu chứa logo Học viện, tiếp theo là khối ngữ cảnh (ví dụ lớp đang quản lý) và các mục chức năng theo vai trò. Thanh này thu gọn được từ 264 px xuống 72 px. Ở trạng thái thu gọn, chỉ còn icon, và tên chức năng hiện dưới dạng tooltip khi rê chuột. Trạng thái thu gọn được lưu trong `localStorage` với khoá `qlnp.navCollapsed`.
- **Thanh công cụ phía trên** cố định khi cuộn trang. Bên trái là nút thu gọn thanh điều hướng và đường dẫn trang hiện tại. Bên phải là ngày hiện tại, nút đổi lớp (chỉ với vai trò trung đội) và menu tài khoản. Menu tài khoản gồm thông tin tài khoản, đổi mật khẩu, bảo mật và đăng xuất.
- **Chân trang** hiển thị thông tin bản quyền và liên hệ của Học viện.

Mã giao diện dùng chung nằm trong `Components/SidebarBrand.razor`, `Components/AccountMenu.razor` và `Components/TopbarDate.razor`. Kiểu dáng của khung trang nằm ở khối cuối tệp `wwwroot/css/site.css`. Logic thu gọn và tooltip nằm trong `wwwroot/js/app.js`.

Mỗi dòng trong bảng đơn nghỉ có nút In. Nút này mở trang `/in-don/{id}` chứa mẫu in của đơn, và người dùng in hoặc lưu PDF bằng chức năng in của trình duyệt. Ứng dụng đặt chiều rộng tối thiểu 1.360 px và chưa hỗ trợ màn hình điện thoại.

## 12. Kiểm thử

Dự án có hai bộ kiểm thử. Cả hai dùng thư mục và database tạm, không đụng tới `AppData` thật.

```powershell
dotnet build QuanLyNghiPhep.sln -c Release
dotnet run --project Tests/QuanLyNghiPhep.Tests.csproj -c Release
node Tests/http_smoke.mjs
```

- `Tests/Program.cs` là chương trình console kiểm tra hồi quy nghiệp vụ. Bộ này không dùng `dotnet test`, và dừng ngay ở kiểm tra thất bại đầu tiên. Phạm vi gồm phân quyền, bảo toàn lịch sử, quản lý tài khoản, nâng cấp schema và JSON, nhập Excel và CSV, nhiều repository đồng thời, rollback khi ghi lỗi, đăng nhập và đổi mật khẩu.
- `Tests/http_smoke.mjs` kiểm tra các luồng HTTP trên một bản cài tạm. Bộ này cần bản build Release và Node.js 20 trở lên.

**Tình trạng tại lần chạy ngày 15/11/2026.** Bộ kiểm thử nghiệp vụ qua 121 kiểm tra đầu tiên, sau đó dừng ở kiểm tra `Incomplete spreadsheet headers are rejected` (`Tests/Program.cs`, dòng 395). Kiểm tra này yêu cầu bộ nhập CSV từ chối tệp chỉ có ba cột STT, Tên, Lớp. Bộ nhập hiện tại lại chấp nhận tệp đó. Các kiểm tra phía sau dòng 395 chưa được chạy. Bộ kiểm thử HTTP chưa được chạy trong lần kiểm tra này.

Giao diện chưa có bộ kiểm thử tự động.

## 13. Cấu trúc thư mục

```text
QuanLyNghiPhep.sln
README.md
start.ps1                     Script khởi chạy nhanh
docs/
  PhanTichBaiToan.md          Phân tích nghiệp vụ và ràng buộc dữ liệu
QuanLyNghiPhep/
  Program.cs                  Cấu hình dịch vụ, Identity, endpoint tài khoản
  appsettings.json            Tham số Leave
  Components/
    App.razor                 Khung HTML gốc
    Pages/                    Màn hình theo vai trò và các trang con
    Layout/                   Layout mặc định
    AccountMenu.razor         Menu tài khoản và popup đổi mật khẩu
    SidebarBrand.razor        Logo đầu thanh điều hướng
    TopbarDate.razor          Ngày hiện tại trên thanh công cụ
    Icon.razor                Bộ icon SVG
  Models/                     Đối tượng nghiệp vụ và hằng số trạng thái
  Data/                       DbContext, lưu trữ SQLite, dữ liệu mẫu
  Services/                   Phân quyền, nghiệp vụ, tài khoản, Excel, sao lưu
  AppData/                    Database, khoá và tệp sinh khi chạy (không đưa lên Git)
  Properties/                 launchSettings.json
  wwwroot/                    CSS, JavaScript, hình ảnh, tệp Excel mẫu
Tests/
  QuanLyNghiPhep.Tests.csproj
  Program.cs                  Kiểm thử hồi quy nghiệp vụ
  http_smoke.mjs              Kiểm thử HTTP
```

## 14. Giới hạn hiện tại

- Hệ thống không có thời khoá biểu, điểm danh, tổng kết chuyên cần hoặc đánh giá điều kiện dự thi. Đường dẫn `/academic` trả về mã 404. Các bảng thời khoá biểu và điểm danh từ phiên bản cũ được giữ nguyên khi nâng cấp, nhưng không có nghiệp vụ nào dùng tới.
- Admin chỉ tạo được từng tài khoản trên giao diện. Hệ thống chưa có chức năng nhập tài khoản hàng loạt.
- Báo cáo đầu ra hiện chỉ có định dạng Excel.
- Mỗi cặp database chỉ chạy được với một tiến trình máy chủ, như đã trình bày ở mục 4.2.
- Giao diện chưa hỗ trợ màn hình nhỏ hơn 1.360 px.
