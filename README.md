# MiTV — Reproductor de IPTV para Windows

**MiTV** es un reproductor de IPTV nativo para Windows, portable (sin instalador), liviano y pensado para verse en TV o monitor 16:9. Escrito en C# con WPF (.NET 10), sin XAML — toda la interfaz se construye por código.

> **Creador original:** [panchocespedes2016](https://github.com/panchocespedes2016)

---

## Características

### Secciones
- **Inicio** — Home inteligente: "Continuar viendo" (películas y episodios a medias, con barra de progreso), "Canales destacados" (los más sintonizados), "Películas populares" y "Series" (lo más reciente del catálogo).
- **TV en vivo** — Lista de canales con logos, número de canal, estrella de favoritos y EPG del programa actual. Categorías en barra lateral con conteo. Filtros: agrupar por categoría, favoritos primero, ocultar canales sin EPG.
- **Películas** — Cuadrícula de pósters con ficha de detalle (género, duración, calificación ★, sinopsis, reparto, tráiler visual mientras carga), botón Reproducir/Continuar, Descargar y estrella de favoritos. Insignia ✓ en las ya vistas.
- **Series** — Ficha con póster, temporadas por pestañas, lista de episodios con estado (✓ visto, ⏵ tiempo restante), continuar donde quedaste, descarga por episodio y favoritos.
- **Deportes** — Detección automática de categorías deportivas por lista.
- **Guía (EPG)** — Parrilla estilo TV con columna de categorías, columna de canales fija, ventana configurable (horas atrás/adelante), línea de "ahora", detalle del programa al hacer clic, vista previa de video muteado, programar grabaciones con clic derecho.
- **Búsqueda** — Buscar en canales, películas y series.

### Reproductor (VLC integrado)
- Reproducción real con LibVLC (no web).
- OSD auto-ocultable: play/pausa, saltos (-10m, -1m, -10s, +10s, +1m, +10m configurables), volumen, velocidad (0.5x–2x), aspecto, pantalla completa (F), pistas de audio y subtítulos.
- **Timeshift en RAM** — Pausa y retrocede la TV en vivo (configurable 30 min a 4 h). La barra muestra el atraso y lo disponible.
- **Grabación (DVR)** — Graba el canal en vivo (botón ⏺ REC) o programa grabaciones desde la guía (clic derecho en un programa). Las programadas se graban en segundo plano.
- **Reproducción en segundo plano** — El audio sigue al salir del reproductor.
- **Enviar a VLC** — Abre el stream en VLC externo con un botón.
- Panel de info (programa actual con barra de progreso) y reloj arrastrable sobre el video.
- Clic derecho en el reproductor abre menú con todas las funciones en palabras.
- Atajos de teclado globales (funcionan aunque el video tenga el foco): flechas, F, I, V, Escape, etc.

### Listas y EPG
- **Listas M3U** (con URL de EPG opcional) y **Xtream Codes** (servidor + usuario + contraseña).
- Sincronización en segundo plano al abrir la app.
- **Editor de categorías** — Activa/desactiva categorías por lista (al desactivar se borra su caché; al reactivar se descarga de nuevo).
- **EPG** — Descarga automática (Xtream: `xmltv.php`; M3U: URL de la lista), caché local, emparejamiento por tvg-id o nombre. Fuente manual opcional. Retención configurable.
- **Favoritos** — Estrella en canales, películas, series y en la guía EPG. Categoría ★ Favoritos en cada sección.
- **Historial** — Últimos 15 elementos abiertos por sección.

### Personalización
- **4 temas**: Oscuro, Claro, Azul, Vino.
- **2 idiomas**: Español, English.
- Pestaña inicial configurable.
- Estilo de subtítulos completo (tamaño, color de fuente, contorno, fondo, opacidad).
- Caché de imágenes en disco (2 GB, portable junto al exe).

### Portable
Todo vive junto al `.exe`: listas, caché, favoritos, historial, ajustes, grabaciones. Sin instalador, sin registro de Windows. Copia la carpeta y llévala.

---

## Requisitos

- **Windows 10/11** (64 bits).
- **.NET 10 SDK** (solo para compilar; el programa ya compilado no lo necesita si usas la versión framework-dependent con el runtime instalado, o publícalo self-contained).

## Cómo compilar

```powershell
# Desde la carpeta del proyecto:
dotnet build MiTV.csproj -c Release -r win-x64
```

El ejecutable queda en `bin\Release\net10.0-windows\win-x64\`.

> **Importante:** compila siempre con `-r win-x64`. Sin ese parámetro el DLL sale en otra carpeta y el de `win-x64` queda obsoleto.

### Dependencias NuGet (se restauran solas)
- `LibVLCSharp.WPF` 3.9.0
- `VideoLAN.LibVLC.Windows` 3.0.21 (VLC portable en `libvlc/win-x64/`)

---

## Cómo usarlo

1. Abre **Ajustes → Listas IPTV → Agregar lista**.
2. Elige **M3U** (nombre + URL + EPG opcional) o **Xtream Codes** (servidor + usuario + contraseña).
3. Marca la lista como **activa** y sincroniza.
4. Navega por las secciones. Toca un canal para verlo, una película/serie para abrir su ficha.

### Estructura de archivos (junto al exe)
| Archivo | Contenido |
|---|---|
| `playlists.json` | Tus listas IPTV |
| `content-cache.json` | Canales/películas/series descargados |
| `epg-cache.xml` | Guía de programación |
| `imgcache/` | Pósters y logos (caché) |
| `mitv-*.json` | Ajustes, favoritos, historial, estado |
| `recordings/` | Grabaciones y descargas |

---

## Estructura del código

Todo el código es C# sin XAML. Archivos principales:

| Archivo | Qué hace |
|---|---|
| `App.cs` | Punto de entrada |
| `MainWindow.cs` | Ventana principal, barra de título personalizada, pestañas |
| `Ui.cs` | Componentes reutilizables (botones, tarjetas, filas) |
| `Views.cs` | Vistas: Home, TV, Películas, Series, Deportes, fichas de detalle |
| `PlayerView.cs` | Reproductor VLC, OSD, timeshift, grabación |
| `EpgView.cs` / `EpgStore.cs` | Parrilla EPG y descarga/parseo de XMLTV |
| `PlaylistLoader.cs` | Descarga de listas M3U/Xtream |
| `PlaylistView.cs` | Gestión de listas |
| `Timeshift.cs` | Servidor HLS local sobre TcpListener (buffer en RAM) |
| `Recording.cs` | Grabación manual y programada |
| `ImgCache.cs` | Caché de imágenes en disco |
| `WatchHistory.cs` | Historial y progreso de visualización |
| `CatFilter.cs` / `CatEditorView.cs` | Filtro y editor de categorías por lista |
| `Lang.cs` / `Theme.cs` | Internacionalización y temas |
| `Playback.cs` / `VodSettings.cs` / `EpgSettings.cs` / `LiveSettings.cs` | Ajustes por sección |

---

## Hazlo tuyo

- **Cambia el tema**: edita `Theme.cs` (colores por rol) o agrega el tuyo en `SettingsView.cs`.
- **Agrega un idioma**: `Lang.cs` usa español como clave; agrega tu diccionario.
- **Ajusta la interfaz**: todo es código C# — los tamaños, colores y textos están en `Ui.cs` y las vistas.
- **Versión**: se define en `<Version>` dentro de `MiTV.csproj`.

---

## Licencia

MIT — úsalo, modifícalo y compártelo libremente, manteniendo el crédito al creador original.

---

*MiTV v1.0.0 — Reproductor de IPTV portable para Windows.*
