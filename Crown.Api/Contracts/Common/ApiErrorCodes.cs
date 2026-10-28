namespace Crown.Contracts.Common;

public static class ApiErrorCodes
{
    public const string CannotDeactivateOwnAccount = "cannot_deactivate_own_account";
    public const string CannotDeleteOwnAccount = "cannot_delete_own_account";
    public const string ProtectedAdminLoginImmutable = "protected_admin_login_immutable";
    public const string ProtectedAdminRoleImmutable = "protected_admin_role_immutable";
    public const string ProtectedAdminCannotDelete = "protected_admin_cannot_delete";

    public const string UserNameRequired = "user_name_required";
    public const string DisplayNameRequired = "display_name_required";
    public const string RoleRequired = "role_required";
    public const string PasswordRequired = "password_required";
    public const string CurrentPasswordRequired = "current_password_required";
    public const string CurrentPasswordInvalid = "current_password_invalid";
    public const string RoleNotFound = "role_not_found";

    public const string PasswordTooShort = "password_too_short";
    public const string PasswordRequiresDigit = "password_requires_digit";
    public const string PasswordRequiresLower = "password_requires_lower";
    public const string PasswordRequiresUpper = "password_requires_upper";
    public const string PasswordRequiresNonAlphanumeric = "password_requires_non_alphanumeric";
    public const string DuplicateUserName = "duplicate_user_name";
    public const string DuplicateEmail = "duplicate_email";
    public const string InvalidUserName = "invalid_user_name";

    public const string LoginCredentialsRequired = "login_credentials_required";

    public const string ServiceNameRequired = "service_name_required";
    public const string DefaultUnitRequired = "default_unit_required";
    public const string UnsupportedMeasurementUnit = "unsupported_measurement_unit";
    public const string InvalidQuantityForUnit = "invalid_quantity_for_unit";
    public const string DefaultUnitPriceNegative = "default_unit_price_negative";

    public const string RestoreConfirmRequired = "restore_confirm_required";
    public const string BackupFileNotFound = "backup_file_not_found";
    public const string BackupAlreadyRunning = "backup_already_running";
    public const string BackupFailed = "backup_failed";
    public const string BackupResticDisabled = "backup_restic_disabled";
    public const string BackupInvalidFile = "backup_invalid_file";
    public const string BackupFileTooLarge = "backup_file_too_large";
    public const string SessionsConfirmRequired = "sessions_confirm_required";
    public const string TrashClearConfirmRequired = "trash_clear_confirm_required";

    public const string UnsupportedCurrency = "unsupported_currency";

    public const string SpreadsheetImportInvalidFile = "spreadsheet_import_invalid_file";

    public const string SpreadsheetImportMappingRequired = "spreadsheet_import_mapping_required";
    public const string SpreadsheetImportFileTooLarge = "spreadsheet_import_file_too_large";
    public const string SpreadsheetImportXlsNotSupported = "spreadsheet_import_xls_not_supported";
    public const string SpreadsheetImportNoData = "spreadsheet_import_no_data";
    public const string SpreadsheetImportTooManyRows = "spreadsheet_import_too_many_rows";
    public const string WarehouseDisabled = "warehouse_disabled";
    public const string PartNameRequired = "part_name_required";
    public const string QuantityOnHandNegative = "quantity_on_hand_negative";
    public const string WarehousePartCodeRequired = "warehouse_part_code_required";
    public const string WarehousePartCodeImmutable = "warehouse_part_code_immutable";
    public const string InsufficientWarehouseStock = "insufficient_warehouse_stock";
    public const string WarehousePartNotFound = "warehouse_part_not_found";
    public const string WarehousePartNotArchived = "warehouse_part_not_archived";
    public const string InternalServerError = "internal_server_error";
    public const string InvalidTimeZone = "invalid_time_zone";
    public const string UnsupportedLogoContentType = "unsupported_logo_content_type";
    public const string LogoDataRequired = "logo_data_required";
    public const string InvalidLogoData = "invalid_logo_data";
    public const string InvalidLogoSize = "invalid_logo_size";
    public const string InvalidLogoFormat = "invalid_logo_format";
    public const string LogoContentTypeMismatch = "logo_content_type_mismatch";

    public const string HostPowerDisabled = "host_power_disabled";
    public const string HostPowerConfirmRequired = "host_power_confirm_required";
    public const string HostPowerFailed = "host_power_failed";
    public const string HostPowerAlreadyRunning = "host_power_already_running";

    public const string NetplanDisabled = "netplan_disabled";
    public const string NetplanConfirmRequired = "netplan_confirm_required";
    public const string NetplanInvalid = "netplan_invalid";
    public const string NetplanReadFailed = "netplan_read_failed";
    public const string NetplanWriteFailed = "netplan_write_failed";
    public const string NetplanApplyFailed = "netplan_apply_failed";

    public const string SupportWebhookFailed = "support_webhook_failed";
    public const string LogServiceInvalid = "log_service_invalid";
}
