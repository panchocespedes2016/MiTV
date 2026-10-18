namespace MiTV
{
    public class Channel
    {
        public string Name, Programme, Icon;
        public string Logo; // URL del logo (opcional)
        public int Progress;
        public int Number; // número de canal (0 = no mostrar)
        public string Tag = ""; // ej: "catchup"
        public Channel(string n, string p, string i, int pr) { Name = n; Programme = p; Icon = i; Progress = pr; }
    }

    public class Movie
    {
        public string Title, Dur, Tag, Cat;
        public int Year;
        public Movie(string t, int y, string d, string tag, string cat) { Title = t; Year = y; Dur = d; Tag = tag; Cat = cat; }
    }

    public class Show
    {
        public string Title, Seasons;
        public Show(string t, string s) { Title = t; Seasons = s; }
    }

    public static class Data
    {
        public static readonly Channel[] Channels = new Channel[]
        {
            new Channel("Action Movies", "Programme Title", "🎬", 55),
            new Channel("Action Sports", "Live: Partido en directo", "⚽", 80),
            new Channel("Noticias 24", "Noticiero de la noche", "📰", 20),
            new Channel("Cine Clásico", "Programme Title", "🎞️", 35),
            new Channel("Kids TV", "Dibujos animados", "🧸", 65),
            new Channel("Música Hits", "Top 40 semanal", "🎵", 45),
        };

        public static readonly Channel[] Sports = new Channel[]
        {
            new Channel("ESPN Deportes", "Fútbol en vivo", "⚽", 70),
            new Channel("Fox Sports", "Béisbol: 7ma entrada", "⚾", 60),
            new Channel("NBA TV", "Medio tiempo", "🏀", 50),
        };

        public static readonly Movie[] Movies = new Movie[]
        {
            new Movie("Action Hero", 2026, "2h 10m", "HD", "Acción"),
            new Movie("Live Action", 2025, "1h 48m", "4K", "Acción"),
            new Movie("Noche de Neón", 2026, "2h 02m", "HD", "Drama"),
            new Movie("El Último Viaje", 2024, "1h 55m", "", "Aventura"),
            new Movie("Risa Total", 2025, "1h 35m", "HD", "Comedia"),
            new Movie("Misterio Total", 2026, "2h 20m", "4K", "Drama"),
        };

        public static readonly Show[] Shows = new Show[]
        {
            new Show("Action Series", "3 temporadas"),
            new Show("La Casa Vecina", "5 temporadas"),
            new Show("Detectives", "2 temporadas"),
            new Show("Espacio Profundo", "1 temporada"),
        };

        public static readonly string[] Recent = new string[] { "news", "sports", "movies 2026" };
        public static readonly string[] MovieCats = new string[] { "Todo", "Acción", "Drama", "Comedia", "Aventura" };
    }
}
