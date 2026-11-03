-- Очистка бизнес-данных после нагрузочного теста.
-- СОХРАНЯЕТ: AspNetUsers / Roles / UserRoles / Claims / Logins / Tokens, AppSettings.
-- НЕ трогает файлы вложений на диске — их чисти отдельно (ATTACHMENTS_HOST_PATH).

BEGIN;

TRUNCATE TABLE
  "RequestAttachments",
  "WarehouseStockMovements",
  "EquipmentMovements",
  "VehicleFuelRefuels",
  "VehicleRepairs",
  "VehicleTrips",
  "DocumentationFiles",
  "DocumentationFolders",
  "ServiceRequests",
  "CatalogParts",
  "WarehouseParts",
  "EquipmentAssets",
  "SpecialVehicleUnits",
  "Vehicles",
  "ParkingLots",
  "AuditLogEntries"
RESTART IDENTITY CASCADE;

COMMIT;
