# Etikit summary

## User

Quiero que me expliques:

1. cómo hace la creación de los diseños: si utiliza el canvas del html para dibujar, si para esto utiliza alguna librería en especial, las clases que se utilizan dentro del código, etc.
2. cómo hace la edición de los diseños: cómo carga un diseño guardado en un archivo y lo dibuja en el tablero, las lcases involucradas dentro del código, etc.
3. la mejor forma para guardar estos diseños en una base de datos relacional.

## 1. Cómo se crean/dibujan los diseños

**No usa un `<canvas>` visible para dibujar el tablero.** El editor es DOM + CSS de React: un `<div>` blanco con elementos posicionados absolutamente. El `<canvas>` de HTML5 aparece solo **fuera de pantalla** para medir texto (`document.createElement('canvas')` en `getFontMetricsPx` y `getElementSize`, en `src/services/label-service.ts`), no para renderizar la etiqueta.

Flujo de creación:

1. **Nueva etiqueta**: el modal toma nombre, preset de tamaño, protocolo y DPI; llama a `resetToNew` en `src/App.tsx`, que construye un `EditorState` inicial con `createDefaultPrintSettings()` y `createDefaultGridSettings()` y limpia el historial.
2. **Añadir elemento**: la barra principal llama a `addElement(type)`. Ahí `LabelService.createDefaultElement(...)` crea el objeto del tipo correspondiente (`text`, `barcode`, `qrcode`, `line`, `rectangle`) con `id` aleatorio, posición inicial y valores por protocolo. Luego `pushState` lo inserta en `state.elements`.
3. **Tablero**: en `App.tsx`, el div contenedor tiene `width = labelSize.width * zoom` y `height = labelSize.height * zoom`. `zoom` significa **px por milímetro** (default 4; el modo Auto usa un `ResizeObserver` para ajustarlo). La rejilla es un `background-image` CSS; las reglas son un componente SVG aparte (`MillimeterRuler`).
4. **Cada elemento** se renderiza con `DraggableElement`, que envuelve a `ElementRenderer`:

| Tipo             | Render real                                                                       |
| ---------------- | --------------------------------------------------------------------------------- |
| Texto            | `<div>` con `fontSize`, `fontFamily`, `transform: scale(...)`, soporte multilínea |
| Código de barras | `<Barcode>` de **`react-barcode`**, escalado horizontalmente                      |
| QR               | `<QRCodeSVG>` de **`qrcode.react`**                                               |
| Línea            | `<svg><line /></svg>` nativo                                                      |
| Rectángulo       | `<div>` con `border` o `background-color`                                         |

La rotación y las correcciones de origen se aplican con `transform: translate(...) rotate(...)` en `ElementRenderer`. `getElementVisualMetadata` calcula la caja sin rotar, la caja rotada y los desplazamientos necesarios.

Interacción:

- **Mover**: `react-draggable`; en `onStop`, `handleDrag` convierte px → unidades del protocolo, aplica snap a rejilla si está activa, y actualiza vía `updateElement`.
- **Redimensionar**: no usa librería; son 4 `<button>` esquina con eventos `pointerdown/move/up` y `setPointerCapture`. `handleResize` recalcula `width/height` (o `size`, o `x2/y2` en líneas) y compensa el anclaje. El QR fuerza escala uniforme.
- **Selección, borrado, flechas, Ctrl+C/X/V/Z/Y**: un `useEffect` con `window.addEventListener('keydown', ...)`.
- **Historial**: hook `useHistory` con `past/present/future`, máximo 50 estados. La selección usa `replaceState`, por lo que no crea entradas de undo.

No hay clases orientadas a objetos para el editor: son componentes funcionales (`App`, `DraggableElement`, `ElementRenderer`, `MillimeterRuler`, `CodePreviewPanel`, etc.). Las “clases” formales del repo son los drivers: `TPCLDriver` y `ZPLDriver`, que implementan la interfaz `LabelDriver` (`generate`/`parse`). El modelo TS está en `src/types.ts`: `LabelElement` y sus variantes, `LabelTemplate`, `EditorState`, `PrintSettings`, `GridSettings`.

Bibliotecas relevantes: `react-draggable`, `react-barcode`, `qrcode.react`, `lucide-react` (iconos), `clsx` + `tailwind-merge` (clases CSS) y Tailwind v4 vía plugin de Vite. No hay Fabric.js, Konva ni motor de canvas.

## 2. Cómo se carga un diseño guardado y se dibuja

Hay tres orígenes distintos:

**A. JSON guardado con “Save Template”**  
`saveTemplate` serializa exactamente un `LabelTemplate` a JSON. Es el único formato con ida y vuelta fiel.

**B. Archivos `.etec` / `.ezpl` del input “Load Template”**

1. `App.loadTemplate` recibe el `File` y delega en `LabelService.loadTemplate(file)`.
2. `loadTemplate` lee el `ArrayBuffer` y primero intenta decodificar como UTF-8 estricto y hacer `JSON.parse`; si el JSON trae `elements: []`, lo usa directamente.
3. Si no es JSON, busca el driver según la extensión (`supportedExtensions`: `.etec` para TPCL, `.ezpl` para ZPL), decodifica **windows-1252 primero** y luego UTF-8 como fallback, y llama a `driver.parse(...)`.
4. El resultado se normaliza: `printSettings` se rellena con defaults, el legacy `zplDotsPerMm` se convierte a DPI, el DPI se redondea a 203/300/600, y `width/height` se redondean a mm.
5. De vuelta en `App`, se limpia el CSV, se cambia a la pestaña Properties y se llama a `resetState({...})` con los elementos, tamaño, nombre, protocolo y ajustes. `resetState` limpia undo/redo.
6. React vuelve a renderizar el tablero con el mismo pipeline: `elements` → `resolvePreviewFields` → `displayElements` → `DraggableElement`/`ElementRenderer`.

Importante: parsear TPCL/ZPL es **reconstructivo**, no una carga exacta. Los `id` se regeneran y algunos atributos se infieren (por ejemplo, ascents de fuente o generación outline). Si quieres conservar el diseño editable, guarda el JSON, no el archivo de impresora.

**C. Borrador local**  
Hay autoguardado en `localStorage` bajo `etikit:local-draft:v1`. Al arrancar, `parseSavedDraft` valida la forma y, si hay contenido recuperable, muestra el prompt. `restoreSavedDraft` también usa `resetState`. El dataset CSV **no** se persiste; los placeholders `{{Campo}}` sí quedan dentro del `content` de cada elemento.

Detalle útil para una BD: el tablero muestra `displayElements`, que pasa por `resolvePreviewFields` (usa `placeholderPreviewValues` o valores de ejemplo). Para exportar se usa `outputElements`, que pasa por `resolveCsvFields` solo cuando hay un CSV cargado. El JSON guardado contiene los `elements` originales con los placeholders sin resolver.

## 3. Mejor forma de guardar en una base de datos relacional

Mi recomendación: **una fila por plantilla con el `LabelTemplate` completo en una columna JSON/JSONB, más columnas relacionales para los campos que necesitas filtrar/ordenar, e historial de revisiones**. Es el ajuste natural a cómo la app ya serializa (`saveTemplate`/`loadTemplate`) y evita que cada propiedad opcional nueva (`qrModel`, `errorCorrection`, `fontRendering`, `zplFieldOriginAscentRatio`, etc.) obligue a migrar esquema.

Ejemplo en PostgreSQL:

```sql
CREATE TABLE label_templates (
  id              uuid PRIMARY KEY,
  owner_id        uuid NOT NULL,
  name            text NOT NULL,
  protocol        text NOT NULL CHECK (protocol IN ('tpcl', 'zpl')),
  width_mm        numeric(10,3) NOT NULL,
  height_mm       numeric(10,3) NOT NULL,
  schema_version  integer NOT NULL DEFAULT 1,
  document        jsonb NOT NULL,
  created_at      timestamptz NOT NULL DEFAULT now(),
  updated_at      timestamptz NOT NULL DEFAULT now(),
  UNIQUE (owner_id, name)
);

CREATE INDEX label_templates_owner_updated_idx
  ON label_templates (owner_id, updated_at DESC);

CREATE INDEX label_templates_document_gin_idx
  ON label_templates USING gin (document);

CREATE TABLE label_template_revisions (
  id            uuid PRIMARY KEY,
  label_id      uuid NOT NULL REFERENCES label_templates(id) ON DELETE CASCADE,
  version       integer NOT NULL,
  document      jsonb NOT NULL,
  created_at    timestamptz NOT NULL DEFAULT now(),
  UNIQUE (label_id, version)
);
```

`document` sería exactamente el objeto que hoy produce `saveTemplate`, idealmente ampliado con `gridSettings`:

```json
{
  "name": "Mi etiqueta",
  "width": 102,
  "height": 76,
  "elements": [ ... ],
  "printSettings": { "quantity": 1, "speed": 3, "darkness": 10, "dpi": 203 },
  "protocol": "tpcl",
  "gridSettings": { "enabled": false, "visible": false, "size": 2 }
}
```

Reglas importantes:

- **No guardes px ni coordenadas convertidas**: `width/height` en mm; `x/y/width/...` en unidades del protocolo (TPCL = 0.1 mm, ZPL = dots según DPI). El px depende del `zoom` de la sesión.
- **`selectedIds` no se persiste**: es estado transitorio de UI.
- **Los archivos `.etec`/`.ezpl` son derivados**: se generan con `driver.generate`; no son la fuente de verdad.
- **CSV separado**: si quieres reutilizar datasets, crea `csv_datasets` y `csv_rows` aparte y deja los placeholders `{{Campo}}` en el JSON de la plantilla. No guardes el contenido ya resuelto.
- **Validación en servidor**: hoy solo `parseSavedDraft` valida parcialmente el borrador local. Para la BD conviene un schema Zod/JSON Schema derivado de `src/types.ts` antes de insertar.
- **Control de concurrencia**: como ya existe autoguardado local, usa `updated_at` o un `version` para evitar que un borrador antiguo sobrescriba una versión más nueva.
- **Versionado**: guarda `schema_version` y migraciones, porque el formato ya ha ido creciendo con campos opcionales.

Si necesitas consultar elementos individualmente (por ejemplo, “todas las etiquetas que usan el campo `{{ProductCode}}`”), añade una tabla híbrida en vez de normalizar todo:

```sql
CREATE TABLE label_elements (
  label_id     uuid NOT NULL REFERENCES label_templates(id) ON DELETE CASCADE,
  element_id   text NOT NULL,
  type         text NOT NULL,
  x            numeric NOT NULL,
  y            numeric NOT NULL,
  rotation     integer NOT NULL DEFAULT 0,
  content      text,
  props        jsonb NOT NULL DEFAULT '{}'::jsonb,
  PRIMARY KEY (label_id, element_id)
);
```

Ahí `props` guarda lo específico por tipo (`fontCode`, `barcodeType`, `size`, `x2/y2`, `errorCorrection`, etc.). Es mejor que columnas específicas por los cinco tipos porque evita decenas de columnas nulas. Pero si solo vas a listar, cargar y guardar plantillas completas, quédate solo con `document` en JSONB: es más simple, más fiel y coincide con el formato que la app ya importa/exporta.
