/*
    Test scenario for Dashboard attendance history on 2026-07-16.

    Scenario:
    1. AppTech attendance:        07:30 - 09:00
    2. Customer request checkin:  09:30 - 13:30
    3. AppTech attendance:        11:00 - 11:30

    Edit @IDNhanVien and @IDYeuCau if you want to target a specific employee/request.
    The script reuses existing AppTech/customer/request data and only inserts TblCheckinHistory rows.
*/

SET XACT_ABORT ON;

DECLARE @Ngay date = '20260716';
DECLARE @IDNhanVien int = NULL; -- Example: 12. Leave NULL to use the first employee found.
DECLARE @IDYeuCau int = NULL; -- Example: 34. Leave NULL to use the latest request with customer/location.
DECLARE @IDKhachHang int = NULL;
DECLARE @IDDiaDiemKhachHang int = NULL;
DECLARE @IDKhachHangAppTech int = NULL;
DECLARE @IDDiaDiemAppTech int = NULL;
DECLARE @LatAppTech decimal(18, 10) = NULL;
DECLARE @LongAppTech decimal(18, 10) = NULL;
DECLARE @LatKhachHang decimal(18, 10) = NULL;
DECLARE @LongKhachHang decimal(18, 10) = NULL;
DECLARE @CheckinImage nvarchar(500) = NULL;
DECLARE @CheckoutImage nvarchar(500) = NULL;
DECLARE @Tag nvarchar(100) = N'TEST_DASHBOARD_20260716';

IF @IDNhanVien IS NULL
BEGIN
    SELECT TOP (1) @IDNhanVien = ID
    FROM dbo.TblNhanVien
    ORDER BY ID;
END;

IF @IDYeuCau IS NULL
BEGIN
    SELECT TOP (1)
        @IDYeuCau = yc.ID,
        @IDKhachHang = yc.IDKhachHang,
        @IDDiaDiemKhachHang = yc.IDDiaDiem
    FROM dbo.TblYeuCau AS yc
    WHERE yc.IDKhachHang IS NOT NULL
      AND yc.IDDiaDiem IS NOT NULL
    ORDER BY yc.ID DESC;
END
ELSE
BEGIN
    SELECT
        @IDKhachHang = yc.IDKhachHang,
        @IDDiaDiemKhachHang = yc.IDDiaDiem
    FROM dbo.TblYeuCau AS yc
    WHERE yc.ID = @IDYeuCau;
END;

SELECT TOP (1)
    @IDKhachHangAppTech = kh.ID,
    @IDDiaDiemAppTech = dd.ID,
    @LatAppTech = dd.LatAddress,
    @LongAppTech = dd.LongAddress
FROM dbo.TblKhachHangDiaDiem AS dd
INNER JOIN dbo.TblKhachHang AS kh ON kh.ID = dd.IDKhachHang
WHERE kh.MaKhachHang LIKE N'Apptech%'
  AND ISNULL(dd.TrangThaiSuDung, 1) = 1
ORDER BY kh.ID, dd.ID;

SELECT
    @LatKhachHang = dd.LatAddress,
    @LongKhachHang = dd.LongAddress
FROM dbo.TblKhachHangDiaDiem AS dd
WHERE dd.ID = @IDDiaDiemKhachHang;

SELECT TOP (1) @CheckinImage = ImgPath
FROM dbo.TblCheckinHistory
WHERE ImgPath IS NOT NULL AND LTRIM(RTRIM(ImgPath)) <> N''
ORDER BY ID DESC;

SELECT TOP (1) @CheckoutImage = ImgPathCheckOut
FROM dbo.TblCheckinHistory
WHERE ImgPathCheckOut IS NOT NULL AND LTRIM(RTRIM(ImgPathCheckOut)) <> N''
ORDER BY ID DESC;

SET @CheckinImage = COALESCE(@CheckinImage, N'/uploads/checkin/test-dashboard-checkin.jpg');
SET @CheckoutImage = COALESCE(@CheckoutImage, @CheckinImage, N'/uploads/checkin/test-dashboard-checkout.jpg');

IF @IDNhanVien IS NULL
    THROW 50001, 'Missing @IDNhanVien. Please set an employee ID.', 1;

IF @IDYeuCau IS NULL OR @IDKhachHang IS NULL OR @IDDiaDiemKhachHang IS NULL
    THROW 50002, 'Missing request/customer/location. Please set @IDYeuCau to an existing request with IDKhachHang and IDDiaDiem.', 1;

IF @IDKhachHangAppTech IS NULL OR @IDDiaDiemAppTech IS NULL
    THROW 50003, 'Cannot find an active AppTech location where TblKhachHang.MaKhachHang LIKE Apptech%.', 1;

BEGIN TRANSACTION;

DELETE FROM dbo.TblCheckinHistory
WHERE IDNhanVien = @IDNhanVien
  AND ThoiDiem >= @Ngay
  AND ThoiDiem < DATEADD(day, 1, @Ngay)
  AND (
      GhiChuNhanVien LIKE N'%' + @Tag + N'%'
      OR GhiChuCheckOut LIKE N'%' + @Tag + N'%'
  );

INSERT INTO dbo.TblCheckinHistory (
    IDKhachHang,
    IDNhanVien,
    ThoiDiem,
    IDYeuCau,
    IDDiaDiem,
    IsCheckIn,
    LongAddress,
    LatAddress,
    ImgPath,
    GhiChuNhanVien,
    CheckInType,
    ThoiDiemCheckOut,
    LongAddressCheckOut,
    LatAddressCheckOut,
    ImgPathCheckOut,
    GhiChuCheckOut
)
VALUES
(
    @IDKhachHangAppTech,
    @IDNhanVien,
    DATEADD(minute, 7 * 60 + 30, CAST(@Ngay AS datetime)),
    NULL,
    @IDDiaDiemAppTech,
    1,
    @LongAppTech,
    @LatAppTech,
    @CheckinImage,
    N'Lượt 07:30 - ' + @Tag,
    N'ChamCong',
    DATEADD(minute, 9 * 60, CAST(@Ngay AS datetime)),
    @LongAppTech,
    @LatAppTech,
    @CheckoutImage,
    N'Lượt 09:00 - ' + @Tag
),
(
    @IDKhachHang,
    @IDNhanVien,
    DATEADD(minute, 9 * 60 + 30, CAST(@Ngay AS datetime)),
    @IDYeuCau,
    @IDDiaDiemKhachHang,
    1,
    @LongKhachHang,
    @LatKhachHang,
    @CheckinImage,
    N'Lượt 09:30 - ' + @Tag,
    NULL,
    DATEADD(minute, 13 * 60 + 30, CAST(@Ngay AS datetime)),
    @LongKhachHang,
    @LatKhachHang,
    @CheckoutImage,
    N'Lượt 13:30 - ' + @Tag
),
(
    @IDKhachHangAppTech,
    @IDNhanVien,
    DATEADD(minute, 11 * 60, CAST(@Ngay AS datetime)),
    NULL,
    @IDDiaDiemAppTech,
    1,
    @LongAppTech,
    @LatAppTech,
    @CheckinImage,
    N'Lượt 11:00 - ' + @Tag,
    N'ChamCong',
    DATEADD(minute, 11 * 60 + 30, CAST(@Ngay AS datetime)),
    @LongAppTech,
    @LatAppTech,
    @CheckoutImage,
    N'Lượt 11:30 - ' + @Tag
);

SELECT
    ch.ID,
    ch.IDNhanVien,
    ch.CheckInType,
    ch.IDYeuCau,
    yc.MaYeuCau,
    ch.IDKhachHang,
    kh.TenKhachHang,
    ch.IDDiaDiem,
    dd.DiaChi,
    ch.ThoiDiem,
    ch.ThoiDiemCheckOut,
    ch.GhiChuNhanVien,
    ch.GhiChuCheckOut
FROM dbo.TblCheckinHistory AS ch
LEFT JOIN dbo.TblYeuCau AS yc ON yc.ID = ch.IDYeuCau
LEFT JOIN dbo.TblKhachHang AS kh ON kh.ID = ch.IDKhachHang
LEFT JOIN dbo.TblKhachHangDiaDiem AS dd ON dd.ID = ch.IDDiaDiem
WHERE ch.IDNhanVien = @IDNhanVien
  AND ch.ThoiDiem >= @Ngay
  AND ch.ThoiDiem < DATEADD(day, 1, @Ngay)
  AND (
      ch.GhiChuNhanVien LIKE N'%' + @Tag + N'%'
      OR ch.GhiChuCheckOut LIKE N'%' + @Tag + N'%'
  )
ORDER BY ch.ThoiDiem ASC, ch.ID ASC;

COMMIT TRANSACTION;
