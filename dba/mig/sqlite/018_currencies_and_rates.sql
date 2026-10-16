-- Master currency definitions are shared reference data; exchange rates are company-scoped history.
CREATE TABLE IF NOT EXISTS currencies (
    currency_code TEXT NOT NULL PRIMARY KEY
        CHECK (length(currency_code)=3 AND currency_code=upper(currency_code)),
    currency_name_ar TEXT NOT NULL CHECK (length(trim(currency_name_ar)) > 0),
    currency_name_en TEXT,
    symbol TEXT,
    fraction_name TEXT,
    decimal_places INTEGER NOT NULL DEFAULT 2 CHECK (decimal_places BETWEEN 0 AND 6),
    is_active INTEGER NOT NULL DEFAULT 1 CHECK (is_active IN (0,1)),
    created_at TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP,
    updated_at TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP
);

CREATE TABLE IF NOT EXISTS currency_rates (
    rate_id INTEGER PRIMARY KEY AUTOINCREMENT,
    company_id TEXT NOT NULL,
    from_currency TEXT NOT NULL,
    to_currency TEXT NOT NULL,
    rate NUMERIC NOT NULL CHECK (rate > 0 AND round(rate,8)=rate),
    buy_rate NUMERIC NOT NULL CHECK (buy_rate > 0 AND round(buy_rate,8)=buy_rate),
    sell_rate NUMERIC NOT NULL CHECK (sell_rate > 0 AND round(sell_rate,8)=sell_rate),
    min_rate NUMERIC NOT NULL CHECK (min_rate > 0 AND round(min_rate,8)=min_rate),
    max_rate NUMERIC NOT NULL CHECK (max_rate > 0 AND round(max_rate,8)=max_rate),
    effective_date TEXT NOT NULL,
    notes TEXT,
    created_by TEXT NOT NULL,
    created_at TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP,
    UNIQUE(company_id, from_currency, to_currency, effective_date),
    CHECK (min_rate <= rate AND rate <= max_rate),
    CHECK (buy_rate <= sell_rate),
    FOREIGN KEY(company_id) REFERENCES companies(company_id),
    FOREIGN KEY(from_currency) REFERENCES currencies(currency_code),
    FOREIGN KEY(to_currency) REFERENCES currencies(currency_code)
);

CREATE TABLE IF NOT EXISTS currency_audit_events (
    currency_audit_event_id INTEGER PRIMARY KEY AUTOINCREMENT,
    currency_code TEXT NOT NULL,
    event_type TEXT NOT NULL CHECK (event_type IN ('CREATED','UPDATED','RATE_ADDED')),
    actor TEXT NOT NULL,
    event_at TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP,
    details TEXT,
    FOREIGN KEY(currency_code) REFERENCES currencies(currency_code)
);

CREATE INDEX IF NOT EXISTS ix_currency_rates_lookup
    ON currency_rates(company_id, from_currency, to_currency, effective_date DESC);
CREATE INDEX IF NOT EXISTS ix_currency_audit_events_code
    ON currency_audit_events(currency_code, event_at DESC);

-- Seed common currencies and preserve any company base-currency codes already in use.
INSERT OR IGNORE INTO currencies(currency_code,currency_name_ar,currency_name_en,symbol,decimal_places)
VALUES
 ('USD','الدولار الأمريكي','US Dollar','$',2),
 ('SAR','الريال السعودي','Saudi Riyal','ر.س',2),
 ('JOD','الدينار الأردني','Jordanian Dinar','د.أ',3),
 ('AED','الدرهم الإماراتي','UAE Dirham','د.إ',2),
 ('KWD','الدينار الكويتي','Kuwaiti Dinar','د.ك',3),
 ('BHD','الدينار البحريني','Bahraini Dinar','د.ب',3),
 ('OMR','الريال العماني','Omani Rial','ر.ع',3),
 ('QAR','الريال القطري','Qatari Riyal','ر.ق',2),
 ('EGP','الجنيه المصري','Egyptian Pound','ج.م',2),
 ('EUR','اليورو','Euro','€',2),
 ('GBP','الجنيه الإسترليني','Pound Sterling','£',2),
 ('TRY','الليرة التركية','Turkish Lira','₺',2),
 ('IQD','الدينار العراقي','Iraqi Dinar','ع.د',3),
 ('SYP','الليرة السورية','Syrian Pound','ل.س',2),
 ('LBP','الليرة اللبنانية','Lebanese Pound','ل.ل',2);

INSERT OR IGNORE INTO currencies(currency_code,currency_name_ar,currency_name_en,symbol,decimal_places)
SELECT upper(currency_code), upper(currency_code), upper(currency_code), NULL, 2
FROM companies
WHERE currency_code IS NOT NULL AND length(trim(currency_code))=3;
