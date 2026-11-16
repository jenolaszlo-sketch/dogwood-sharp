using System.Reflection;

var assembly = Assembly.Load("DogwoodSharp");
Console.WriteLine($"Loaded {assembly.GetName().Name}: discovery foundation only; no policy engine.");
