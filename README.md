# SCO POS Adapter (Vynamic Self-Service → BAF)

**Перенос на ПК с VSS:** см. `INSTALL-SCO-PC.txt`.

Отдельный процесс на ПК SCO. Порт 15001 (FMSK) не использовать.

Библиотека DN: `lib/csPOSAdapterLibrary_4_0.dll` (`IfcIScanConnector` + `IfcAllInboundGroupsBaseImpl`).

Печать eKasa по-прежнему: 1С → `:15001` → FMSKHttpService. Этот процесс к принтеру не ходит.

## Сборка

.NET Framework 4.8 Developer Pack, `dotnet` SDK, платформа **x64**.

```
cd C:\1C\Самокаса\ScoPosAdapter
dotnet build -c Release
```

Отладка в **Visual Studio 2022** (не VS Code / Cursor): F5 по консоли.

```
cd bin\Release\net48
ScoPosAdapter.exe
```

`--service` — только для Windows-службы. Без аргументов процесс остаётся консольным: так его запускает VSS.

HTTP debug слушает `http://127.0.0.1:15002/` (не всю сеть). Если отказ в доступе:

```
netsh http add urlacl url=http://127.0.0.1:15002/ user=Everyone
```

Не запускайте второй экземпляр на тот же prefix (HTTP.sys 183). Если HTTP debug не поднялся, связь с VSS всё равно работает.

## Проверка без VSS

`UseErpMock=true` в `App.config` (по умолчанию).

```
GET  http://127.0.0.1:15002/health
POST http://127.0.0.1:15002/simulate/getItemDetails
Content-Type: application/json

{"itemCode":"8584000123456","context":"scanning"}
```

Ожидается `IFC_OK`, описание `Creme 50 ml`, `itemPrice` 1299 (центы). Другой код — `IFC_ERROR_ITEM_UNKNOWN`.

`/health` показывает `vssEnabled` / `vssConnected` / `vssLastError`.

## Включение живого VSS

Адаптер — **TCP-клиент**: VSS слушает 17002–17005, адаптер подключается. Если VSS ещё не запущен, адаптер повторяет каждые `VssReconnectSeconds`.

1. Порты main/cancel/posEvents/cancelPosEvents из конфигурации VSS прописать в `App.config`.
2. `EnableVss=true`. Канал authentication по умолчанию выключен (`VssAuthEnabled=false`).
3. Один экземпляр exe: либо VSS запускает адаптер, либо служба, не оба.

Пока `EnableVss=false` — только HTTP-simulate и mock 1С.

Реализованный цикл чека (мок): `getStatus` / `getGlobalProperties` / `signOn` / `startTransaction` / `getItemDetails` / `addItem` / `voidEntry` / `voidTransaction` / `preparePayment` / `unpreparePayment` / оплата / `transactionFinished` / `cancel`. Остальные методы Interface 4.0 отвечают `IFC_ERROR_OTHER` и **не рвут** канал.

При `UseErpMock=true` оплата принимается локально. При `false` товары идут в 1С, оплата отклоняется до интеграции с BAF.

## 1С

`UseErpMock=false`, `ErpBaseUrl` = публикация ИБ (`.../hs`). `LaneId` — серийный номер кассы ККМ. Адаптер вызывает реализованные методы HTTP-сервиса SCO: status, global-properties, sign-on/off, items/resolve, transactions, items, items/{entryId}/void, loyalty, payment/prepare, payment/card, special. `itemPrice` — целое число в центах. `ErpAuth`: `None` | `Os` | `Basic`. `ErpPaymentTimeoutSeconds` — таймаут Adyen (по умолчанию 120). Finish/void чека, unprepare и фискал в BAF ещё заглушки.

## Служба Windows

```
powershell -ExecutionPolicy Bypass -File install-service.ps1
```

Скрипт ставит `binPath` с `--service` и автоперезапуск при сбое.
