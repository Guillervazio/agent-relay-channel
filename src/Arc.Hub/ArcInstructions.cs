namespace Arc.Hub;

/// <summary>
/// Lo que el canal le cuenta de sí mismo a quien se conecta, en el handshake de MCP.
///
/// Existe para que ningún proyecto tenga que pegar estas reglas en su propio
/// <c>CLAUDE.md</c>: pegarlas significa una copia por repositorio, y las copias se
/// separan. Aquí hay una sola, y viaja con el hub.
///
/// No repite lo que cada herramienta ya dice de sí misma —para eso están sus
/// <c>[Description]</c>— y no nombra ningún proyecto, ninguna máquina ni ningún
/// agente concreto: quién está al otro lado se averigua con <c>arc_agents</c>.
/// </summary>
internal static class ArcInstructions
{
    internal const string Text = """
        ARC es un canal de peticiones entre agentes de distintos proveedores que trabajan en
        paralelo sobre copias separadas del mismo repositorio. Sirve para preguntarse cosas y
        bloquear hasta la respuesta, no para narrar el progreso.

        Al empezar tu turno, mira el buzón con arc_inbox antes de ponerte a trabajar: puede
        haber alguien bloqueado esperándote ahora mismo. Si hay una petición, contéstala antes
        de seguir con lo tuyo — cada minuto que tardas es un minuto que el otro pasa parado.

        Cuándo escribir:

        - arc_ask cuando necesitas su respuesta para continuar: un contrato que define él, una
          decisión sobre código que está tocando, confirmar una suposición antes de construir
          encima. Bloquea hasta que conteste.
        - arc_note cuando sólo informas de un hecho consumado: "subí la rama", "cambié esta
          firma". No espera respuesta.
        - No escribas para contar por dónde vas. El canal es para lo que el otro necesita
          saber.

        Con quién: arc_agents dice quién está en el canal y cuándo se le vio por última vez. No
        supongas un destinatario que no aparezca ahí.

        Qué mandar: **referencias, no contenido**. El otro tiene otra copia del repositorio, así
        que una ruta no significa nada para él hasta que hayas hecho push. Sube la rama primero y
        manda sus coordenadas en refs — rama, commit, ficheros. El cuerpo está limitado a 256 KB.

        Cuando vence un plazo no se ha perdido nada: la petición sigue viva en el buzón del otro
        y se recupera con arc_await, o con arc_inbox y unanswered. Lo normal es seguir con otra
        parte de tu trabajo y recoger la respuesta después.

        **Nunca esperéis los dos a la vez**: quemaríais los dos turnos sin que nadie avance. Si
        vas a preguntar algo que llevará tiempo, dilo con arc_note y sigue trabajando.
        """;
}
