using System.Text;
using Arc.Cli;

// Todo lo demás vive en CliRunner, donde un test puede darle otra entrada, otra
// salida y otro transporte. Aquí sólo se le entregan los de verdad.
Console.OutputEncoding = new UTF8Encoding(false);

CliRunner runner = new CliRunner(Console.Out, Console.Error, Console.In);
return await runner.RunAsync(args);
