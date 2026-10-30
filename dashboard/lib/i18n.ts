// Bilingual (AR/EN) dictionary + direction. القاموس ثنائي اللغة.
export type Lang = "en" | "ar";

export const dir = (lang: Lang): "rtl" | "ltr" => (lang === "ar" ? "rtl" : "ltr");

type Dict = Record<string, { en: string; ar: string }>;

export const t: Dict = {
  appTitle: { en: "Loyalty Platform", ar: "منصة الولاء" },
  connect: { en: "Connect", ar: "اتصال" },
  apiBase: { en: "API base URL", ar: "رابط الواجهة البرمجية" },
  apiKey: { en: "API key", ar: "مفتاح الواجهة" },
  bootstrapToken: { en: "Bootstrap token (first run)", ar: "رمز التهيئة (أول تشغيل)" },
  rootName: { en: "Root application name", ar: "اسم التطبيق الجذر" },
  bootstrap: { en: "Bootstrap root", ar: "تهيئة الجذر" },
  save: { en: "Save", ar: "حفظ" },
  dashboard: { en: "Dashboard", ar: "لوحة التحكم" },
  applications: { en: "Applications", ar: "التطبيقات" },
  campaigns: { en: "Campaigns", ar: "الحملات" },
  members: { en: "Members", ar: "الأعضاء" },
  events: { en: "Events", ar: "الأحداث" },
  name: { en: "Name", ar: "الاسم" },
  nameAr: { en: "Arabic name", ar: "الاسم بالعربية" },
  status: { en: "Status", ar: "الحالة" },
  active: { en: "Active", ar: "مفعّل" },
  create: { en: "Create", ar: "إنشاء" },
  activate: { en: "Activate", ar: "تفعيل" },
  pause: { en: "Pause", ar: "إيقاف" },
  trigger: { en: "Trigger event", ar: "حدث التفعيل" },
  priority: { en: "Priority", ar: "الأولوية" },
  points: { en: "Points", ar: "النقاط" },
  externalCustomerId: { en: "External customer ID", ar: "معرّف العميل الخارجي" },
  wallet: { en: "Wallet", ar: "المحفظة" },
  activeBalance: { en: "Active balance", ar: "الرصيد الفعّال" },
  lookup: { en: "Look up", ar: "بحث" },
  requestId: { en: "Request ID", ar: "معرّف الطلب" },
  awardedPoints: { en: "Awarded points", ar: "النقاط الممنوحة" },
  submitTestEvent: { en: "Submit test purchase", ar: "إرسال عملية شراء تجريبية" },
  amount: { en: "Amount (IQD)", ar: "المبلغ (دينار)" },
  notConnected: { en: "Not connected. Enter your API base and key.", ar: "غير متصل. أدخل رابط الواجهة والمفتاح." },
  transactions: { en: "Ledger transactions", ar: "حركات دفتر الأستاذ" },
  spendPerPoint: { en: "Spend per point", ar: "الإنفاق لكل نقطة" },
  loyaltySettings: { en: "Loyalty settings", ar: "إعدادات الولاء" },
  saved: { en: "Saved", ar: "تم الحفظ" },
  segments: { en: "Segments", ar: "الشرائح" },
  valueProfile: { en: "Value profile", ar: "ملف القيمة" },
  lifecycleStage: { en: "Lifecycle stage", ar: "مرحلة دورة الحياة" },
  purchaseCount: { en: "Purchases", ar: "عدد المشتريات" },
  netSpend: { en: "Net spend", ar: "صافي الإنفاق" },
  pointsEarned: { en: "Points earned", ar: "النقاط المكتسبة" },
  activeSegments: { en: "Active segments", ar: "الشرائح الفعّالة" },
  field: { en: "Field", ar: "الحقل" },
  operator: { en: "Operator", ar: "العامل" },
  value: { en: "Value", ar: "القيمة" },
  validate: { en: "Validate", ar: "تحقّق" },
  cvmSettings: { en: "CVM settings", ar: "إعدادات إدارة قيمة العميل" },
};

export function tr(key: string, lang: Lang): string {
  return t[key]?.[lang] ?? key;
}
