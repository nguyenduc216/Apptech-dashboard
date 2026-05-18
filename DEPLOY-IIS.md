# Deploy IIS

Project nay la ASP.NET Core MVC tren `.NET 8`, vi vay IIS se host qua `AspNetCoreModuleV2`, khong chay nhu mot site static.

## 1. Yeu cau tren may chu IIS

- Cai `IIS` voi role `Static Content`
- Cai `.NET 8 Hosting Bundle` tren may chu
- Sau khi cai Hosting Bundle, `restart` IIS bang `iisreset`

Neu khong cai Hosting Bundle, IIS se tra loi `HTTP Error 500.31`.

## 2. Publish project

Tu thu muc project, chay:

```powershell
dotnet publish .\ApptechDashboard.csproj -c Release -o .\publish\iis
```

Hoac dung publish profile da them:

```powershell
dotnet publish .\ApptechDashboard.csproj /p:PublishProfile=IIS
```

Thu muc deploy se la `publish\iis\` va da bao gom `web.config`.

## 3. Tao site tren IIS

1. Mo `IIS Manager`
2. Tao `Application Pool` moi:
   - `.NET CLR version`: `No Managed Code`
   - `Managed pipeline mode`: `Integrated`
3. Tao `Website` hoac `Application` moi, `Physical path` tro den thu muc `publish\iis`
4. Gan site vao Application Pool vua tao
5. Cap quyen read/execute cho user cua App Pool tren thu muc publish

## 4. Bat HTTPS tren IIS

Code da co san:

- `UseHttpsRedirection()`
- `UseHsts()` khi khong phai `Development`

De site chay `https`, can cau hinh tai IIS:

1. Co chung chi SSL hop le tren server
2. Vao site trong `IIS Manager` -> `Bindings...`
3. Them binding:
   - `Type`: `https`
   - `Port`: `443`
   - `Host name`: domain cua ban, vi du `dashboard.example.com`
   - `SSL certificate`: chon certificate tuong ung
4. Giu binding `http` neu muon tu dong redirect sang `https`

Neu ban co domain that, can dam bao DNS da tro ve may chu IIS.

## 5. Cau hinh production

- Sua `publish\iis\appsettings.json` hoac them `appsettings.Production.json` neu can
- Neu muon bat environment production ro rang, them bien moi truong trong IIS:
  - `ASPNETCORE_ENVIRONMENT=Production`

## 6. Kiem tra va debug

- Browse site tu IIS
- Thu truy cap truc tiep bang `https://ten-domain-cua-ban`
- Neu loi startup, tam bat log stdout trong `web.config`:

```xml
<aspNetCore processPath="dotnet"
            arguments=".\ApptechDashboard.dll"
            stdoutLogEnabled="true"
            stdoutLogFile=".\logs\stdout"
            hostingModel="inprocess" />
```

Sau do tao san thu muc `logs` ben trong thu muc publish va recycle app pool.

## 7. Deploy update

Moi lan cap nhat:

```powershell
dotnet publish .\ApptechDashboard.csproj /p:PublishProfile=IIS
```

Sau do copy lai noi dung trong `publish\iis\` len server IIS.
