using System.Collections.Generic;
using System.Text;

public static class LSystemGenerator
{
    // Default ruleset - can be swapped for different plant types
    private static readonly Dictionary<char, string> DefaultRules = new()
    {
        { 'X', "F-[[X]+X]+F[+FX]-X" },  // Branching pattern
        { 'F', "FF" }                     // Growth
    };

    public static string Generate(string axiom, int iterations, Dictionary<char, string> rules = null)
    {
        rules ??= DefaultRules;
        
        string current = axiom;
        StringBuilder next = new StringBuilder(current.Length * 2);

        for (int i = 0; i < iterations; i++)
        {
            next.Clear();
            
            foreach (char c in current)
            {
                if (rules.TryGetValue(c, out string replacement))
                    next.Append(replacement);
                else
                    next.Append(c);  // Keep constants unchanged
            }
            
            current = next.ToString();
        }

        return current;
    }
    
    // Stochastic version - adds controlled randomness
    public static string GenerateStochastic(string axiom, int iterations, float variance, int seed)
    {
        var random = new System.Random(seed);
        
        // Multiple rule options for X - chosen randomly
        var stochasticRules = new Dictionary<char, string[]>
        {
            { 'X', new[] { 
                "F+[[X]-X]-F[-FX]+X",    // Standard
                "F-[[X]+X]+F[+FX]-X",    // Mirrored
                "F[+X][-X]FX"            // Simpler
            }},
            { 'F', new[] { "FF" } }
        };
        
        string current = axiom;
        StringBuilder next = new StringBuilder();

        for (int i = 0; i < iterations; i++)
        {
            next.Clear();
            
            foreach (char c in current)
            {
                if (stochasticRules.TryGetValue(c, out string[] options))
                {
                    // Pick random rule variant
                    int index = random.Next(options.Length);
                    next.Append(options[index]);
                }
                else
                {
                    next.Append(c);
                }
            }
            
            current = next.ToString();
        }

        return current;
    }
}