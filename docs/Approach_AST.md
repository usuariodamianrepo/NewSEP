# Abstract Syntax Tree (AST) - árboles de sintaxis abstracta

Yo evitaría intentar mantener un diccionario ZPL ↔ TPCL completo de forma manual. Es un problema que normalmente nunca termina porque:

- ZPL tiene cientos de comandos, parámetros y variaciones según firmware.
- TPCL (TSC Printer Command Language) también tiene diferencias entre modelos.

Hay comandos equivalentes 1:1, otros 1:n y otros directamente sin equivalente.

Mi recomendación es cambiar el enfoque de "traductor por diccionario" a un motor intermedio de representación.

## Opción 1 (la que más recomiendo): Generar un AST / modelo intermedio

TPCL -> AST
ZPL  -> AST

AST -> Preview SVG

TPCL  ---------\
                -> AST -> Renderizador
ZPL   ---------/

**Ventajas:**

- No necesitas mapear TPCL → ZPL.
- Cada lenguaje tiene su parser.
- Añadir EPL, DPL o ESC/POS sería sencillo.
- El preview queda desacoplado de la impresora.

## Opción 2: Completar el diccionario por frecuencia de uso
