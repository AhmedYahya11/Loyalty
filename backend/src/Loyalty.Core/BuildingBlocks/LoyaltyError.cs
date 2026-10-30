namespace Loyalty.Core.BuildingBlocks;

/// <summary>
/// A machine-readable error with the HTTP status and bilingual (English + Arabic) messages.
/// The API selects the message language from the Accept-Language header. النظام ثنائي اللغة.
/// </summary>
public sealed record LoyaltyError(int StatusCode, string Code, string MessageEn, string MessageAr, string? MessageKu = null)
{
    /// <summary>Default message (English), used for exception text and logs.</summary>
    public string Message => MessageEn;

    /// <summary>Localized message: Arabic when <paramref name="arabic"/> is true, else English.</summary>
    public string Localized(bool arabic) => arabic ? MessageAr : MessageEn;

    /// <summary>Localized message by language code ("en"/"ar"/"ku"). Kurdish falls back to Arabic
    /// (shared script) when a dedicated message is not provided.</summary>
    public string Localized(string lang) => lang switch
    {
        "ku" => MessageKu ?? MessageAr,
        "ar" => MessageAr,
        _ => MessageEn,
    };

    // Auth / scope
    public static LoyaltyError InvalidCredential() => new(401, "invalid_credential",
        "Missing or invalid API key.", "مفتاح الواجهة البرمجية مفقود أو غير صالح.");
    public static LoyaltyError CredentialDisabled() => new(403, "credential_disabled",
        "The account, application, or an ancestor is inactive.", "الحساب أو التطبيق أو أحد التطبيقات الأصل غير مُفعّل.");
    public static LoyaltyError ApplicationOutsideSubtree() => new(403, "application_outside_subtree",
        "The target application is not visible to this credential.", "التطبيق المستهدف غير مرئي لهذا الاعتماد.");

    // Validation
    public static LoyaltyError ValidationFailed(string messageEn, string? messageAr = null) =>
        new(422, "validation_failed", messageEn, messageAr ?? messageEn);
    public static LoyaltyError UnsupportedEventContract(string messageEn, string? messageAr = null) =>
        new(400, "unsupported_event_contract", messageEn, messageAr ?? messageEn);

    // Not found / conflict
    public static LoyaltyError NotFound(string what) => new(404, "not_found",
        $"{what} was not found.", $"{what} غير موجود.");
    public static LoyaltyError Conflict(string code, string messageEn, string? messageAr = null) =>
        new(409, code, messageEn, messageAr ?? messageEn);

    // Loyalty config
    public static LoyaltyError LoyaltyNotConfigured() => new(422, "LOYALTY_NOT_CONFIGURED",
        "No loyalty settings on the application or any ancestor.", "لا توجد إعدادات ولاء على التطبيق أو أي تطبيق أصل.");
    public static LoyaltyError LoyaltyNotActive() => new(422, "LOYALTY_NOT_ACTIVE",
        "Effective loyalty settings are paused, closed, or outside their active dates.",
        "إعدادات الولاء الفعّالة متوقفة أو مغلقة أو خارج فترة التفعيل.");
    public static LoyaltyError PointsUnitInUse() => new(409, "POINTS_UNIT_IN_USE",
        "The effective points unit cannot change after ledger activity.",
        "لا يمكن تغيير وحدة النقاط الفعّالة بعد وجود حركات في دفتر الأستاذ.");

    // Infra
    public static LoyaltyError DurableAcceptanceFailed() => new(503, "durable_acceptance_failed",
        "The event could not be durably accepted. Retry later.",
        "تعذّر قبول الحدث بشكل دائم. أعد المحاولة لاحقًا.");
}

/// <summary>Thrown by handlers/domain to signal a mapped API error.</summary>
public sealed class LoyaltyException(LoyaltyError error) : Exception(error.MessageEn)
{
    public LoyaltyError Error { get; } = error;
}
