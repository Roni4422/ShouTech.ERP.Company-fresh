-- Keep the PostgreSQL permission catalog aligned with SQLite's enterprise
-- permission catalog. This permission is required by PrmWin and Permissions.cs.
INSERT INTO permissions
    (permission_code, permission_name, group_code, feature_code, operation_code,
     name_ar, name_en, description, display_order, is_sensitive)
VALUES
    ('SYS.PERMISSIONS.MANAGE', 'Permission Management', 'SYSTEM', 'PERMISSIONS', 'MANAGE',
     'إدارة الصلاحيات', 'Permission Management', 'إدارة صلاحيات المستخدمين', 228, TRUE)
ON CONFLICT (permission_code) DO UPDATE SET
    permission_name = EXCLUDED.permission_name,
    group_code = EXCLUDED.group_code,
    feature_code = EXCLUDED.feature_code,
    operation_code = EXCLUDED.operation_code,
    name_ar = EXCLUDED.name_ar,
    name_en = EXCLUDED.name_en,
    description = EXCLUDED.description,
    display_order = EXCLUDED.display_order,
    is_sensitive = EXCLUDED.is_sensitive;

-- The permission is deliberately granted only to active administrator roles.
-- User and Viewer grants remain governed by the policy established in 006_permissions.sql.
INSERT INTO role_permissions (role_id, permission_id)
SELECT r.role_id, p.permission_id
FROM roles r
CROSS JOIN permissions p
WHERE r.role_code = 'ROLE_ADMINISTRATOR'
  AND r.is_active = TRUE
  AND p.permission_code = 'SYS.PERMISSIONS.MANAGE'
  AND p.is_active = TRUE
ON CONFLICT DO NOTHING;
