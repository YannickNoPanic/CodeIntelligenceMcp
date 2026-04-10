# EXAMPLES.md — Wiki Output and Tool Response Examples

## Python Project Examples

### get_python_wiki — FastAPI Project

Tool call:
```json
{ "workspace": "my-api", "includePatterns": true, "includeMetrics": true }
```

Output:
```
# Python Project Wiki

Generated: 2026-04-09 12:00:00 UTC

## Module Structure

### src/myapp/
  __init__.py
    Imports: myapp.core, myapp.db

  main.py
    Functions: create_app, lifespan
    Imports: fastapi, myapp.api.v1.router, myapp.db

  dependencies.py
    Functions: get_db, get_current_user, require_admin
    Imports: fastapi, sqlalchemy.orm, myapp.models.user

### src/myapp/api/v1/
  router.py
    Functions: include_routers
    Imports: fastapi, myapp.api.v1.endpoints

  endpoints/users.py
    Functions: get_users, get_user, create_user, update_user, delete_user
    Decorators: router.get, router.post, router.put, router.delete
    Imports: fastapi, myapp.schemas.user, myapp.services.user_service

  endpoints/items.py
    Functions: list_items, get_item, create_item
    Imports: fastapi, myapp.schemas.item, myapp.services.item_service

### src/myapp/models/
  user.py
    Classes: User (Base)
    Imports: sqlalchemy, myapp.db.base

  item.py
    Classes: Item (Base), ItemCategory (Base)
    Imports: sqlalchemy, myapp.db.base

### src/myapp/schemas/
  user.py
    Classes: UserBase (BaseModel), UserCreate (UserBase), UserRead (UserBase), UserUpdate (BaseModel)
    Imports: pydantic

  item.py
    Classes: ItemBase (BaseModel), ItemCreate (ItemBase), ItemRead (ItemBase)
    Imports: pydantic, uuid

### src/myapp/services/
  user_service.py
    Classes: UserService
    Functions: get_users, get_user_by_id, create_user, update_user, delete_user
    Imports: sqlalchemy.orm, myapp.models.user, myapp.schemas.user

  item_service.py
    Classes: ItemService
    Functions: list_items, get_item, create_item
    Imports: sqlalchemy.orm, myapp.models.item

### tests/
  conftest.py
    Functions: engine, session, client
    Imports: pytest, sqlalchemy, fastapi.testclient

  test_users.py
    Functions: test_get_users, test_create_user, test_update_user, test_delete_user
    Imports: pytest

## Dependencies

- fastapi (>=0.110.0) [pyproject.toml]
- sqlalchemy (~=2.0) [pyproject.toml]
- pydantic (>=2.6) [pyproject.toml]
- uvicorn[standard] (>=0.27.0) [pyproject.toml]
- python-jose[cryptography] (>=3.3.0) [pyproject.toml]
- passlib[bcrypt] (>=1.7.4) [pyproject.toml]
- pytest (>=8.0) [dev] [pyproject.toml]
- httpx (>=0.27.0) [dev] [pyproject.toml]

## Patterns Detected

- Framework: FastAPI
- Framework: Pydantic (Pydantic v2 models)
- Framework: SQLAlchemy
- Framework: Pytest
- Async Functions: 18 of 32 functions
- Route Decorators: @router.get (5), @router.post (3), @router.put (2), @router.delete (2)
- Pydantic Models: 8 classes inherit BaseModel
- SQLAlchemy Models: 3 classes inherit Base

## Metrics

- Python Files (.py): 18
- Classes: 14
- Functions: 32
- Packages (with __init__.py): 6
- Test Files: 3
```

---

### py_get_file — Single File Analysis

Tool call:
```json
{ "workspace": "my-api", "filePath": "src/myapp/api/v1/endpoints/users.py" }
```

Output:
```json
{
  "filePath": "src/myapp/api/v1/endpoints/users.py",
  "functions": [
    {
      "name": "get_users",
      "lineStart": 18,
      "lineEnd": 28,
      "parameters": [
        { "name": "skip", "typeHint": "int", "defaultValue": "0" },
        { "name": "limit", "typeHint": "int", "defaultValue": "100" },
        { "name": "db", "typeHint": "Session", "defaultValue": "Depends(get_db)" },
        { "name": "current_user", "typeHint": "User", "defaultValue": "Depends(get_current_user)" }
      ],
      "returnTypeHint": "list[UserRead]",
      "decorators": ["router.get('/', response_model=list[UserRead])"],
      "isAsync": true,
      "isMethod": false
    },
    {
      "name": "create_user",
      "lineStart": 31,
      "lineEnd": 44,
      "parameters": [
        { "name": "user_in", "typeHint": "UserCreate", "defaultValue": null },
        { "name": "db", "typeHint": "Session", "defaultValue": "Depends(get_db)" }
      ],
      "returnTypeHint": "UserRead",
      "decorators": ["router.post('/', response_model=UserRead, status_code=201)"],
      "isAsync": true,
      "isMethod": false
    }
  ],
  "classes": [],
  "imports": [
    { "module": "fastapi", "names": ["APIRouter", "Depends", "HTTPException"], "isRelative": false, "line": 1 },
    { "module": "sqlalchemy.orm", "names": ["Session"], "isRelative": false, "line": 2 },
    { "module": "myapp.schemas.user", "names": ["UserCreate", "UserRead", "UserUpdate"], "isRelative": false, "line": 3 },
    { "module": "myapp.services.user_service", "names": ["UserService"], "isRelative": false, "line": 4 },
    { "module": "myapp.dependencies", "names": ["get_db", "get_current_user"], "isRelative": false, "line": 5 }
  ],
  "exportedNames": [],
  "detectedFrameworks": ["FastAPI", "SQLAlchemy"]
}
```

---

### py_find_function — Cross-File Function Search

Tool call:
```json
{ "workspace": "my-api", "functionName": "create" }
```

Output:
```json
[
  {
    "filePath": "src/myapp/api/v1/endpoints/users.py",
    "functionName": "create_user",
    "lineStart": 31,
    "lineEnd": 44,
    "isAsync": true,
    "decorators": ["router.post('/', response_model=UserRead, status_code=201)"],
    "parameters": [
      { "name": "user_in", "typeHint": "UserCreate", "defaultValue": null },
      { "name": "db", "typeHint": "Session", "defaultValue": "Depends(get_db)" }
    ]
  },
  {
    "filePath": "src/myapp/services/user_service.py",
    "functionName": "create_user",
    "lineStart": 52,
    "lineEnd": 64,
    "isAsync": false,
    "decorators": [],
    "parameters": [
      { "name": "self", "typeHint": null, "defaultValue": null },
      { "name": "db", "typeHint": "Session", "defaultValue": null },
      { "name": "user_in", "typeHint": "UserCreate", "defaultValue": null }
    ]
  },
  {
    "filePath": "src/myapp/api/v1/endpoints/items.py",
    "functionName": "create_item",
    "lineStart": 29,
    "lineEnd": 41,
    "isAsync": true,
    "decorators": ["router.post('/', response_model=ItemRead, status_code=201)"],
    "parameters": [
      { "name": "item_in", "typeHint": "ItemCreate", "defaultValue": null },
      { "name": "db", "typeHint": "Session", "defaultValue": "Depends(get_db)" }
    ]
  }
]
```

---

## JavaScript/TypeScript Project Examples

### get_js_wiki — Nuxt 3 Project

Tool call:
```json
{ "workspace": "my-nuxt-app", "includePatterns": true, "includeMetrics": true }
```

Output:
```
# JavaScript/TypeScript Project Wiki

Generated: 2026-04-09 12:00:00 UTC

## Module Structure

### pages/
  index.vue [SFC: script-setup + ts]
    Imports: @/composables/useProducts, @/composables/useAuth

  products/
    index.vue [SFC: script-setup + ts]
      Composables: useProducts, useRoute
      Props: none (page component)

    [id].vue [SFC: script-setup + ts]
      Composables: useProducts, useRoute, useSeoMeta

  auth/
    login.vue [SFC: script-setup + ts]
      Composables: useAuth, useRouter
      Emits: none

### layouts/
  default.vue [SFC: script-setup + ts]
    Imports: @/components/Nav, @/components/Footer

  admin.vue [SFC: script-setup + ts]
    Imports: @/components/AdminNav, @/composables/useAuth

### components/
  ProductCard.vue [SFC: script-setup + ts]
    Props: product, showActions
    Emits: add-to-cart, view-details
    Composables: useCart, useCurrency

  DataTable.vue [SFC: script-setup + ts]
    Props: columns, rows, loading
    Emits: row-click, sort, page-change

  forms/
    LoginForm.vue [SFC: script-setup + ts]
      Props: redirectTo
      Emits: submit, cancel

### composables/
  useAuth.ts
    Functions: useAuth [exported]
    Exports: useAuth

  useProducts.ts
    Functions: useProducts [exported]
    Exports: useProducts, ProductFilter

  useCart.ts
    Functions: useCart [exported]
    Exports: useCart

### stores/
  cart.ts
    Functions: useCartStore [exported]
    Exports: useCartStore

  auth.ts
    Functions: useAuthStore [exported]
    Exports: useAuthStore

### server/api/
  products/
    index.get.ts
      Functions: default [exported default]
      Imports: h3, @/server/utils/db

    [id].get.ts
      Functions: default [exported default]

  auth/
    login.post.ts
      Functions: default [exported default]

### utils/
  formatters.ts
    Functions: formatPrice, formatDate, truncate
    Exports: formatPrice, formatDate, truncate

  validators.ts
    Functions: validateEmail, validatePassword
    Exports: validateEmail, validatePassword, ValidationResult

### types/
  product.ts
    Interfaces: Product, ProductVariant, ProductFilter [all exported]
    TypeAliases: ProductId, CategorySlug [exported]

  auth.ts
    Interfaces: User, AuthSession, LoginPayload [all exported]

## Dependencies

- nuxt (^3.10.0) [package.json]
- vue (^3.4.0) [package.json]
- pinia (^2.1.7) [package.json]
- @pinia/nuxt (^0.5.1) [package.json]
- @tanstack/vue-query (^5.28.0) [package.json]
- @vueuse/nuxt (^10.9.0) [package.json]
- zod (^3.22.4) [package.json]
- typescript (~5.4) [devDependencies]
- @nuxt/devtools (^1.0.8) [devDependencies]

## Patterns Detected

- Framework: Nuxt 3
- Framework: Vue 3 (Composition API)
- Framework: Pinia (state management)
- TypeScript: 28 of 34 files
- ESM Imports: 34 files (0 CommonJS)
- Vue SFC Components: 14 (.vue files)
- Script Setup: 14 of 14 Vue components
- Async Functions: 11 of 38

### Nuxt Conventions
- Pages (file-based routing): 5 in pages/
- Composables (auto-imported): 3 in composables/
- Pinia Stores (auto-imported): 2 in stores/
- Layouts: 2 in layouts/
- Server API Routes: 3 in server/api/

## Metrics

- JavaScript (.js): 0
- TypeScript (.ts/.tsx): 20
- Vue SFC (.vue): 14
- Interfaces: 8
- Type Aliases: 5
- Exported Functions: 24
- Composables: 3
```

---

### js_get_file — Vue SFC Analysis

Tool call:
```json
{ "workspace": "my-nuxt-app", "filePath": "components/ProductCard.vue" }
```

Output:
```json
{
  "filePath": "components/ProductCard.vue",
  "sfcInfo": {
    "blocks": [
      { "tag": "template", "lang": null, "isSetup": false, "lineStart": 1, "lineEnd": 24 },
      { "tag": "script", "lang": "ts", "isSetup": true, "lineStart": 26, "lineEnd": 52 },
      { "tag": "style", "lang": "scss", "isSetup": false, "lineStart": 54, "lineEnd": 68 }
    ],
    "props": ["product", "showActions"],
    "emits": ["add-to-cart", "view-details"],
    "composables": ["useCart", "useCurrency"]
  },
  "scriptAnalysis": {
    "functions": [
      {
        "name": "handleAddToCart",
        "lineStart": 38,
        "lineEnd": 43,
        "parameters": [],
        "isAsync": false,
        "isExported": false,
        "kind": "arrow"
      }
    ],
    "imports": [
      { "source": "vue", "namedImports": ["computed"], "defaultImport": null, "line": 27 },
      { "source": "@/composables/useCart", "namedImports": ["useCart"], "defaultImport": null, "line": 28 },
      { "source": "@/composables/useCurrency", "namedImports": ["useCurrency"], "defaultImport": null, "line": 29 },
      { "source": "@/types/product", "namedImports": ["Product"], "isTypeOnly": true, "line": 30 }
    ],
    "typeAliases": [],
    "interfaces": [],
    "moduleType": "esm"
  }
}
```

---

### js_find_function — Cross-File Function Search

Tool call:
```json
{ "workspace": "my-nuxt-app", "functionName": "useAuth" }
```

Output:
```json
[
  {
    "filePath": "composables/useAuth.ts",
    "functionName": "useAuth",
    "lineStart": 8,
    "lineEnd": 52,
    "isExported": true,
    "isAsync": false,
    "kind": "declaration"
  },
  {
    "filePath": "stores/auth.ts",
    "functionName": "useAuthStore",
    "lineStart": 4,
    "lineEnd": 38,
    "isExported": true,
    "isAsync": false,
    "kind": "arrow"
  }
]
```

---

### js_search — Cross-File Search

Tool call:
```json
{ "workspace": "my-nuxt-app", "query": "formatPrice" }
```

Output:
```json
[
  {
    "filePath": "utils/formatters.ts",
    "lineNumber": 3,
    "context": "function formatPrice"
  },
  {
    "filePath": "utils/formatters.ts",
    "lineNumber": 18,
    "context": "export formatPrice"
  },
  {
    "filePath": "components/ProductCard.vue",
    "lineNumber": 28,
    "context": "import formatPrice from utils/formatters"
  }
]
```

---

## Wiki Output Comparison

The output style follows the established `PowerShellWikiGenerator` pattern:
- Markdown headers for sections
- Indented tree-style directory listing
- Compact inline annotations (no verbose JSON in wiki output)
- Dependencies section with version + source
- Patterns section with counts
- Optional metrics section

This makes wiki output token-efficient and scannable for Claude Code when used as context.
