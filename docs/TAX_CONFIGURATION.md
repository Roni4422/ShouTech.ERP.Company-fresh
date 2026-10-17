# Tax configuration and posting safety

## Company country

- DBA migration `030_company_country.sql` adds the company-level `country` field for SQLite and PostgreSQL.
- Company creation and profile updates persist the country entered in the company setup wizard.
- Country-based fallback rates are used only when the company has a recognized country and no active default tax code is configured.
- Unknown or missing country configuration does not silently default every company to Saudi Arabia.

## Default-rate precedence

1. An active default record in `tax_codes` is authoritative.
2. If no default tax code exists, a recognized company country may use the application's regional fallback rate table.
3. If neither source can determine a rate, invoice initialization is blocked with `TAX-RATE-001`; the application does not guess a tax rate.
4. Database/configuration failures are logged and reported with `TAX-DB-001`. Tax and party lookup failures use `TAX-LOOKUP-001`.

## POS item tax handling

- A configured positive material `tax_rate` is respected.
- Materials marked non-taxable or zero-rated retain a zero rate.
- A taxable material with no positive configured rate uses the company default rate.
- The POS checkout uses the resolved rate without overriding zero-rated items.

## Provider compatibility

Boolean predicates for `is_active`, `is_default`, and `is_deleted` use PostgreSQL boolean literals for PostgreSQL and integer literals for SQLite. Schema changes are owned by `dba/mig`; service code performs reads and validation rather than creating tables.
