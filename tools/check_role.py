import os
import urllib.parse
import psycopg2

password = <REDACTED>
conn = psycopg2.connect(
    host=os.environ.get("PGHOST", "localhost"),
    port=os.environ.get("PGPORT", "5432"),
    dbname=os.environ.get("PGDATABASE", "ragdb"),
    user=os.environ.get("PGUSER", "tigerrag"),
    password=<REDACTED>
)
cur = conn.cursor()

cur.execute('SELECT "Id", "Name", "NormalizedName" FROM "AspNetRoles" WHERE "Name" LIKE %s ORDER BY "Name"', ('Test%',))
roles = cur.fetchall()
print("Test roles:", roles)
for role_id, name, norm in roles:
    cur.execute('SELECT COUNT(*) FROM "AspNetUserRoles" WHERE "RoleId" = %s', (role_id,))
    user_bindings = cur.fetchone()[0]
    print(f"{name}: id={role_id}, normalized={norm}, user_bindings={user_bindings}")

cur.execute("SELECT \"Label\", \"Roles\" FROM \"menu_config_record\" WHERE \"Roles\"::text LIKE '%Test%'")
menus = cur.fetchall()
print("Test menu references:", menus)

conn.close()
