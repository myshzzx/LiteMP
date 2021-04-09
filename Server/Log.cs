using System;

namespace LiteServer
{
    class Log
    {
        [Obsolete("Use server instance logger or make your own for the filterscript (preferred method is to make your own)")]
        public static void LogToConsole(int flag, string module, string message)
        {
            if (module == null || module.Equals(""))
                module = "SERVER";

            switch (flag)
            {
                case 1:
                    Console.ForegroundColor = ConsoleColor.Cyan; Console.WriteLine("[" + DateTime.Now + "] (DEBUG) " + module.ToUpper() + ": " + message);
                    break;
                case 2:
                    Console.ForegroundColor = ConsoleColor.Green; Console.WriteLine("[" + DateTime.Now + "] (SUCCESS) " + module.ToUpper() + ": " + message);
                    break;
                case 3:
                    Console.ForegroundColor = ConsoleColor.DarkYellow; Console.WriteLine("[" + DateTime.Now + "] (WARNING) " + module.ToUpper() + ": " + message);
                    break;
                case 4:
                    Console.ForegroundColor = ConsoleColor.Red; Console.WriteLine("[" + DateTime.Now + "] (ERROR) " + module.ToUpper() + ": " + message);
                    break;
                case 6:
                    Console.ForegroundColor = ConsoleColor.Magenta; Console.WriteLine("[" + DateTime.Now + "] " + module.ToUpper() + ": " + message);
                    break;
                default:
                    Console.WriteLine("[" + DateTime.Now + "] " + module.ToUpper() + ": " + message);
                    break;
            }

            Console.ForegroundColor = ConsoleColor.White;
        }
    }
}
