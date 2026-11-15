namespace MsNotificaciones.Dominio;

/// <summary>Tipos de notificación. El frontend elige el ícono y la acción según el tipo.</summary>
public static class TiposNotificacion
{
    public const string Bienvenida = "BIENVENIDA";
    public const string Recomendacion = "RECOMENDACION";
    public const string Inactividad = "INACTIVIDAD";
    public const string Evento = "EVENTO";
    public const string EventoTec = "EVENTO_TEC";
    public const string Precio = "PRECIO";
}

public sealed record Contenido(string Titulo, string Mensaje, string? Ruta = null);

/// <summary>Primera vez que un usuario entra a la app.</summary>
public static class Bienvenida
{
    public const string Id = "bienvenida";
    public static readonly Contenido Texto = new(
        "¡Bienvenido a EstudiAmbre!",
        "Nos alegra que te nos unás. ¡Es hora de ahorrar!",
        "/plan");
}

/// <summary>Recordatorios por inactividad: 1 día, 1 semana y 1 mes sin entrar.</summary>
public static class Inactividad
{
    public static readonly (int Dias, Contenido Texto)[] Niveles =
    [
        (1, new("¡Recordá entrar a registrar tus gastos!",
            "Un día sin anotar nada. Si gastaste algo, apuntalo ahora que todavía te acordás.", "/registrar")),
        (7, new("¡Hace una semana que no te vemos!",
            "Tus gastos no se anotan solos. Entrá un toque y poné al día tu quincena.", "/registrar")),
        (30, new("¡Un mes sin pasar por aquí!",
            "Nunca es tarde para retomar. Revisá tu plan y empezá de nuevo, aunque sea con un gasto.", "/plan")),
    ];

    /// <summary>Nivel que corresponde a los días sin actividad: 0 (activo), 1, 2 o 3.</summary>
    public static int NivelPara(double diasSinActividad)
    {
        var nivel = 0;
        for (var i = 0; i < Niveles.Length; i++)
            if (diasSinActividad >= Niveles[i].Dias) nivel = i + 1;
        return nivel;
    }
}

/// <summary>
/// Eventos con fecha (se envían solos ese día a medianoche) y eventos sin fecha (solo a mano,
/// con <c>POST /v1/tareas/eventos/{codigo}</c>). El equipo agrega los nuevos aquí.
/// </summary>
public sealed record EventoProgramado(string Codigo, DateOnly? Fecha, string Tipo, Contenido Texto);

public static class Eventos
{
    public static readonly IReadOnlyList<EventoProgramado> Todos =
    [
        new("entrada-tec", null, TiposNotificacion.EventoTec, new(
            "Entrada a clases",
            "Buena suerte en el semestre a nuestros usuarios del TEC, ¡cuiden esos gastos!",
            "/plan")),
        new("gta-vi", new DateOnly(2026, 11, 19), TiposNotificacion.Evento, new(
            "¡GTA VI por fin se estrenó!",
            "Antes de viajar a Vice City, ¡revisá tus balances!",
            "/")),
        new("juegos-del-hambre", new DateOnly(2026, 11, 20), TiposNotificacion.Evento, new(
            "¡Amanecer en la cosecha ya está en cines!",
            "Volvieron Los Juegos del Hambre. Si vas a ir, apartá la entrada y las palomitas en tu presupuesto. ¡Que la suerte esté siempre de tu lado!",
            "/registrar")),
        new("black-friday", new DateOnly(2026, 11, 27), TiposNotificacion.Evento, new(
            "¡Hoy es Black Friday!",
            "Un descuento no es ahorro si no lo necesitabas. Hacé tu lista, compará precios y no te endeudés por una oferta.",
            "/comparar")),
        new("avengers-doomsday", new DateOnly(2026, 12, 17), TiposNotificacion.Evento, new(
            "¡Avengers: Doomsday ya está en cines!",
            "¿Vas a ir a verla? Recordá apuntar todos esos gastos inevitables.",
            "/registrar")),
    ];

    public static EventoProgramado? Buscar(string codigo) =>
        Todos.FirstOrDefault(e => e.Codigo.Equals(codigo, StringComparison.OrdinalIgnoreCase));
}

/// <summary>
/// Recomendaciones diarias (una por día a medianoche). Cada usuario recibe el mes en un orden
/// aleatorio propio y sin repetir: el orden sale de un sorteo con semilla (usuario + mes), así que
/// no hay que guardar cuáles ya recibió y reintentar el mismo día da la misma.
/// </summary>
public static class Recomendaciones
{
    public static readonly IReadOnlyList<Contenido> Todas =
    [
        new("¡No gastés antes de planificar!",
            "Revisá cuánto tenés disponible y cuánto necesitás para lo esencial. Así vas a saber cuándo podés gastar.", "/plan"),
        new("El ahorro también es un gasto",
            "No esperés a que sobre dinero para ahorrar. Separá una pequeña cantidad apenas recibás tu salario, beca o mesada. Aunque sea poco, lo importante es crear el hábito."),
        new("¡Cuidado con los gastos hormiga!",
            "Registrá todo gasto: podrías descubrir que las compras pequeñas suman una cantidad importante al final del mes.", "/registrar"),
        new("¿Necesidad o deseo?",
            "Antes de comprar algo, hacete esa pregunta. No tenés que dejar de darte gustos, pero sí aprender a distinguirlos."),
        new("Establecé un límite para gastos personales",
            "Definí cuánto podés gastar en entretenimiento, salidas y compras personales durante la quincena. Un límite no significa dejar de disfrutar, sino evitar sorpresas.", "/plan"),
        new("Revisá tus suscripciones",
            "Si un poco de dinero desaparece de tu tarjeta, tal vez sean suscripciones que no recordás ni usás. Cancelar las que no necesitás puede liberarte algo todos los meses."),
        new("Hacé un balance de tu semana",
            "Tomate cinco minutos para revisar tus movimientos. ¿En qué gastaste más? ¿Compras innecesarias? Conocer tus hábitos es el primer paso para mejorarlos.", "/"),
        new("Compará antes de comprar",
            "El mismo producto puede tener precios muy distintos según el establecimiento. Antes de comprar, usá el comparador de EstudiAmbre para ver dónde te sale más barato.", "/comparar"),
        new("No confundás descuentos con ahorro",
            "Comprar algo que no necesitás porque tiene 50 % de descuento no es ahorrar 50 %: si no lo ibas a comprar, gastaste de más."),
        new("Hacé una lista antes del súper",
            "Comprar sin lista aumenta las compras que no necesitás. Organizá tus compras según lo que de verdad hace falta y respetá tu presupuesto."),
        new("Revisá el precio por unidad",
            "Un empaque más grande no siempre sale más barato. Compará el precio por kilo, litro o unidad para ver cuál conviene.", "/comparar"),
        new("Aprovechá los productos de temporada",
            "Las frutas y verduras de temporada suelen estar más baratas. Adaptar tus compras a la temporada te ayuda a ahorrar."),
        new("Cuidado con las compras impulsivas",
            "¿Viste algo que querés? Esperá 24 horas antes de decidir. Si después lo seguís necesitando y cabe en tu presupuesto, ahí lo pensás."),
        new("No todo lo barato es buena compra",
            "Fijate en la calidad y la durabilidad: algo barato que tenés que reemplazar a cada rato puede terminar saliendo más caro."),
        new("Dividí tu dinero por categorías",
            "Asigná un monto a cada tipo de gasto: comida, transporte, entretenimiento, estudios y ahorro. Así sabés cuánto podés destinar a cada cosa.", "/plan"),
        new("Creá un fondo para emergencias",
            "Un gasto inesperado puede desordenarte las finanzas. Empezá con aportes pequeños; tu primera meta puede ser cubrir una semana de gastos esenciales."),
        new("Aprovechá los beneficios estudiantiles",
            "Investigá los descuentos para estudiantes en transporte, comida, software, tecnología y entretenimiento. Tu carné puede valer más de lo que creés."),
        new("Prepará tus comidas cuando podás",
            "Comprar comida todos los días sale caro. Preparar almuerzos o meriendas con anticipación te ayuda a gastar menos y comer mejor."),
        new("Evitá financiar pequeños caprichos",
            "No todo tiene que pagarse a cuotas. Antes de usar crédito, revisá intereses y comisiones, y si de verdad podés asumir ese compromiso."),
        new("Registrá tus gastos de inmediato",
            "No confiés en la memoria: anotá cada compra apenas la hacés. Un registro al día te ayuda a decidir mejor.", "/registrar"),
        new("Revisá tus metas de ahorro",
            "Una meta concreta es más fácil de cumplir. En vez de \"quiero ahorrar\", proponete juntar ₡30 000 para algo específico y definí cuánto vas a aportar cada quincena.", "/plan"),
        new("Identificá tus gastos fijos",
            "Alquiler, servicios, transporte y suscripciones se repiten todos los meses. Conocerlos te dice cuánto dinero ya está comprometido antes de empezar a gastar.", "/plan"),
        new("No gastés todo lo que recibís",
            "Recibir dinero no significa gastarlo ya. Reservá una parte para lo que viene y no comprometás todo tu ingreso desde el primer día."),
        new("Investigá antes de comprar tecnología",
            "Antes de comprar un teléfono, una compu o un accesorio, compará especificaciones, precios y garantías. Lo más caro no siempre es lo que mejor te sirve.", "/comparar"),
        new("Cuidado con los gastos compartidos",
            "Cuando salgás con amigos, pónganse de acuerdo en cómo dividir. No asumás pagos que no te tocan y contemplá esas salidas en tu presupuesto."),
        new("Aprendé a reconocer estafas",
            "Desconfiá de ganancias rápidas, inversiones sin riesgo o premios inesperados. Nunca compartás contraseñas, códigos de verificación ni datos bancarios."),
        new("Tu tiempo también vale",
            "A veces la opción más barata implica viajes, filas o costos extra. Tomá en cuenta el transporte y el tiempo antes de decidir dónde comprar."),
        new("No comparés tus finanzas con las de otros",
            "Cada quien tiene ingresos y responsabilidades distintas. Enfocate en mejorar tus hábitos y en metas que tengan sentido para vos."),
        new("Construí tu historial crediticio con cuidado",
            "Si usás tarjeta de crédito, pagá el saldo completo a tiempo y no gastés más de lo que podés cubrir. Un buen manejo del crédito te abre puertas."),
        new("Celebrá tus pequeños avances",
            "¿Gastaste menos esta quincena? ¿Conseguiste una mejor oferta? Reconocelo: construir buenos hábitos es un proceso, no una competencia."),
        new("Tu mejor inversión es aprender",
            "La educación financiera no termina cuando te graduás. Seguí aprendiendo de ahorro, presupuesto, inversión y crédito: entre más entendás tu dinero, mejor vas a decidir."),
    ];

    /// <summary>La recomendación de <paramref name="fecha"/> para el usuario: el mes en orden aleatorio, sin repetir.</summary>
    public static Contenido DelDia(string usuarioId, DateOnly fecha)
    {
        var orden = Enumerable.Range(0, Todas.Count).ToArray();
        new Random(Semilla($"{usuarioId}|{fecha:yyyy-MM}")).Shuffle(orden);
        return Todas[orden[(fecha.Day - 1) % orden.Length]];
    }

    // FNV-1a: estable entre ejecuciones (string.GetHashCode cambia en cada proceso).
    private static int Semilla(string texto)
    {
        unchecked
        {
            var h = 2166136261u;
            foreach (var c in texto) h = (h ^ c) * 16777619u;
            return (int)h;
        }
    }
}
