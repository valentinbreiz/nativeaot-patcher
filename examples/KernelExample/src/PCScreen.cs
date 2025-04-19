using EarlyBird.Conversion;

namespace EarlyBird.PSF
{
    public static unsafe class PCScreenFont
    {
        public static class Default
        {
            // array size is 17403
            public static int Size = 17403;

            public static char* GetUnmanagedFontData()
            {

                string ZapLight = "crVKhgAAAAAgAAAAAQAAAAABAABAAAAAIAAAABAAAAAAAAAAAAAAAAAAAAAf+B/4eB5wDmfmZ+Z/5n/Of55/Pn5+fn5+fn5+f/5//n5+fn4f+B/4AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA//D/8DDAMMAwwDDAMMAwwDDAMMAwwDDAMMAwYDB4MDgAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAABgAGAAwADB//n/+AMAAwAGAAYADAAMAf/5//gwADAAYABgAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAABwAOABwAOABwAOABwAHAAOAAcAA4ABwADgAHAAAAAAH/gf+AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAOAAcAA4ABwADgAHAAOAA4AHAA4AHAA4AHAA4AAAAAAB/4H/gAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAD/8P/w//D/8P/w//D/8P/w//D/8P/w//D/8P/wAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAABgAPAB+AP8B/4P/x//n/+P/wf+A/wB+ADwAGAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAABgAOAB4ANgAGAAYABgYGDAYYBjAAYADAAYADAAYYDDgYeDDYYZjDGAP8A/wAGAAYABgAGAAAAAAAAAAAAAAAAAYADgAeADYABgAGAAYGBgwGGAYwAGAAwAGAAwAGAAwAGHgw/GGGwYYADAA4AGAAwAH+Af4AAAAAAAAAAAAAAAA+AH8AQYABgB8AHwABhkGMfxg+MABgAMABgAMABhgMOBh4MNhhmMMYA/wD/AAYABgAGAAYAAAAAAAAAAAAAAAAAAABgAGAAYABgAGAAYABgAGAAYABgAAAAAAAAAAAAYABgAGAAYABgAGAAYABgAGAAYAAAAAAAAAAAAAAAAAAAAwwDDAMMAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAMAAwADAAYAPAA4AAAAAAAAAAAAAfgD+AcABgAGAAYABgAGAH/gf+AGAAYABgAGAAYABgAGAAYABgAGAAYADgH8AfgAAAAAAAAAAAAAAAAAAAAAAAAAAAAGAAYABgAGAH/gf+AGAAYABgAGAAYABgAGAAYABgAGAAYABgAGAAYAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAABgAGAAYABgB/4H/gBgAGAAYABgAGAAYABgAGAH/gf+AGAAYABgAGAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAGAw8DGYYZjBmMGZgPMAYwAGAAwADAAYADMwN/hszMzMzM2Mzwf7AzAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAP2D/ccx7zG7MZMxgzGDMYMAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAMYwxjDGMAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAGAAwAGAAwAGAAYAAwABgADAAGAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAYAAwABgADAAGAAYADAAYADAAYAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAMMAwwDDAMMAwwBhgDDAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAADDAMMAwwDDAMMBhgMMAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAMMAwwDDAMMAwwGGAwwAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAADDAMMAwwDDAMMAYYAwwAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAGAAYABgAGAAYAAwABgAAAAADAMOBwf+A/wAAAAAA/wH/g4HDAMMAwwDDAAMAAwADAAMPww/DAMMAwwDDAMMAw4HB/4D/AAAAAAAAAAAAAAAAAAAAAAMAw4HB/4D/AAAAAAAAAAAA/MH+w4PDAcMAwwDDAMMAwwDDAMMBw4PB/sD8wADAAMAAwADDAMOBwf+A/wAAABgAGAAYAAAAAAH/gf+AGAAYABgAGAAYABgAGAAYABgAGAAYABgAGAAYABgAGAH/gf+AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAB4AHgAGAAYABgAGAAYABgAGAAYABgAGAAYABgB/4H/gAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAP8B/4OBwwDDAMMAAwADAAOAAf8A/4ABwADAAMAAwwDDAMOBwf+A/wAMAAwADAAYAPAA4AAAAAAAAAAAAAAAAAAAAAAAAAAAAP8B/4OBwwDDAAMAA4AB/wD/gAHAAMAAwwDDgcH/gP8ADAAMAAwAGADwAOAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAABgAGAAYABgAGAAYABgAGAAYABgAGAAYABgAGAAAAAAAAAAYABgAGAAAAAAAAAAAAAAAAAAAAAAAAADDAMMAwwDDAMMAwwDDAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAwwDDAMMAwwDDAMMD/8P/wMMAwwDDAMMD/8P/wMMAwwDDAMMAwwDDAAAAAAAAAAAAAAAAAAAAAAAAABgAGAAYABgB/4P/xxjmGGYYBhgGGAcYA/+B/8AY4BhgGGAYZhhnGOP/wf+AGAAYABgAGAAAAAAAAAAAAAAAAAAAAAAAAAHgY/BmGMYZhhmGGwP2AeYADAAYABgAMABngG/A2GGYYZhjGGYPxgeAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAeAD8Ac4BhgGGAYYBhgDMAHgA8AH4A5wHDmYGxgOGA4YHBw+D/MH4YAAAAAAAAAAAAAAAAAAAAAAAABgAGAAYABgAGAAwAGAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAYADAAMABgAGAAwADAAYABgAGAAYABgAGAAYABgAGAAYAAwADAAGAAYAAwADAAGAAAAAAAAAAAAAAAAAAAAAABgADAAMAAYABgADAAMAAYABgAGAAYABgAGAAYABgAGAAYADAAMABgAGAAwADAAYAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAABgAGAYYYxjBmYDbAH4AfgDbAZmDGMYYYBgAGAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAYABgAGAAYABgAGAf/5//gGAAYABgAGAAYABgAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAABgAGAAYABgAGAAwAGAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA//D/8AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAGAAYABgAAAAAAAAAAAAAAAAAAAAAAAAAAAAAYABgAMAAwAGAAYADAAMABgAGAAwADAAYABgAMAAwAGAAYADAAMABgAGAAwADAAAAAAAAAAAAAAAAAAAAAAAAAAAAAP8B/4OBwwDDAMMAwwDDAMMYwxjDGMMYwwDDAMMAwwDDAMOBwf+A/wAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAYADgAeADYAZgDGAAYABgAGAAYABgAGAAYABgAGAAYABgAGAH/gf+AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA/wH/g4HDAMMAwwDAAMABgAMABgAMABgAMABgAMABgAMAAwAD/8P/wAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAP8B/4OBwwDDAMMAwADAAMABgD8APwABgADAAMMAwwDDAMOBwf+A/wAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAADAAcADwAbADMAYwDDAYMDAwMDAwMD/8P/wAMAAwADAAMAAwADAAMAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAD/8P/wwADAAMAAwADAAMAA/8D/4ABwADAAMAAwADAAMMAw4HB/4D/AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAD8AfwDAAYADAAMAAwADAAN/A/+DgcMAwwDDAMMAwwDDAMOBwf+A/wAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAP/w//AAMAAwAGAAYADAAMABgAGAAwADAAYABgAMAAwADAAMAAwADAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA/wH/g4HDAMMAwwDDAMMAwYGA/wD/AYGDAMMAwwDDAMMAw4HB/4D/AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAP8B/4OBwwDDAMMAwwDDAMMAw4HB/8D+wADAAMAAwADAAYADAP4A/AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAYABgAGAAAAAAAAAAAAAAAAAAAAAAAGAAYABgAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAGAAYABgAAAAAAAAAAAAAAAAAAAAAABgAGAAYABgAGAAwAGAAAAAAAAAAAAAAAAAAAAAAAAAAA4AHAA4AHAA4AHAA4AHAA4ADgAHAAOAAcAA4ABwADgAHAAOAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAD/8P/wAAAAAAAAAAAAAAAA//D/8AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAHAAOAAcAA4ABwADgAHAAOAAcABwAOABwAOABwAOABwAOABwAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAP8B/4OBwwDDAMMAwADAAYADAAYADAAMAAwADAAAAAAAAAAMAAwADAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA/AH+A4cHAw4zDHsMzwzHDMMMwwzDDMMMwwznDH8OPQcAA4AB/wD/AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAGAAYADwAPABmAGYAZgDDAMMAwwGBgYGB/4H/gwDDAMMAxgBmAGYAYAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA/8D/4MBwwDDAMMAwwDDAMMBg/8D/wMBgwDDAMMAwwDDAMMBw/+D/wAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAD/Af+DgcMAwwDDAMMAAwADAAMAAwADAAMAAwADAMMAwwDDgcH/gP8AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAD/AP+AwcDA4MBgwDDAMMAwwDDAMMAwwDDAMMAwwDDAYMDgwcD/gP8AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA//D/8MAAwADAAMAAwADAAMAA/8D/wMAAwADAAMAAwADAAMAA//D/8AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAP/w//DAAMAAwADAAMAAwADAAP/A/8DAAMAAwADAAMAAwADAAMAAwAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA/wH/g4HDAMMAwwDDAAMAAwADAAMPww/DAMMAwwDDAMMAw4HB/4D/AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAwDDAMMAwwDDAMMAwwDDAMMAw//D/8MAwwDDAMMAwwDDAMMAwwDDAMAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAH/gf+AGAAYABgAGAAYABgAGAAYABgAGAAYABgAGAAYABgAGAH/gf+AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAf+B/4AYABgAGAAYABgAGAAYABgAGAAYABgAGBgYGBgYGBw4D/AH4AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAABgHGA4YHBg4GHAY4BnAG4AfAB4AHwAbgBnAGOAYcBg4GBwYDhgHGAOAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAMAAwADAAMAAwADAAMAAwADAAMAAwADAAMAAwADAAMAAwADAAP/w//AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAGAGYAZwDnAOeB54Hmw2bDZmZmZmY8ZjxmGGYYZgBmAGYAZgBmAGYAYAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAwDDgMOAw8DDwMNgw2DDMMMwwxjDGMMMwwzDBsMGwwPDA8MBwwHDAMAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAD/Af+DgcMAwwDDAMMAwwDDAMMAwwDDAMMAwwDDAMMAwwDDgcH/gP8AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAD/wP/gwHDAMMAwwDDAMMAwwDDAcP/g/8DAAMAAwADAAMAAwADAAMAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAP8B/4OBwwDDAMMAwwDDAMMAwwDDAMMAwwDDAMM8w37DxsPDwf+A/wAD8AHwAAAAAAAAAAAAAAAAAAAAAAAAAAP/A/+DAcMAwwDDAMMAwwDDAcP/g/8DDAMGAwYDAwMDAwGDAYMAwwBgAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA/wH/g4HDAMMAwwADAAMAA4AB/wD/gAHAAMAAwADDAMMAw4HB/4D/AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAB//n/+AYABgAGAAYABgAGAAYABgAGAAYABgAGAAYABgAGAAYABgAGAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAMAwwDDAMMAwwDDAMMAwwDDAMMAwwDDAMMAwwDDAMMAwwDDgcH/gP8AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAGAGYAZgBmAGMAwwDDAMGBgYGBgYDDAMMAwwBmAGYAZgA8ADwAGAAYAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAADAA8ADwAPAA8ADwAPAA2GGYYZjxmPGY8ZmZmZmNmw8PDw8OBw4HDAMAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAMAwwDBgYGBgMMAwwBmAGYAPAA8ADwAPABmAGYAwwDDAYGBgYMAwwDAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAADAMMAwYGBgYDDAMMAZgBmADwAPAAYABgAGAAYABgAGAAYABgAGAAYAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAf/B/8ABgAGAAwADAAYABgAMAAwAGAAYADAAMABgAGAAwADAAf/B/8AAAAAAAAAAAAAAAAAAAAAAAAAAAH4AfgBgAGAAYABgAGAAYABgAGAAYABgAGAAYABgAGAAYABgAGAAYABgAGAAfgB+AAAAAAAAAAAAAAAAAAAAAAYABgADAAMAAYABgADAAMAAYABgADAAMAAYABgADAAMAAYABgADAAMAAYABgADAAMAAAAAAAAAAAAAAAAAAAAAAfgB+AAYABgAGAAYABgAGAAYABgAGAAYABgAGAAYABgAGAAYABgAGAAYABgB+AH4AAAAAAAAAAAAAAAAAGAA8AGYAwwGBgwDGAGAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAB//n/+AAAAAAAAAAAAAAAAAAABgAGAAYABgAGAAMAAYAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAP8B/4OBwwDAAMAAwADA/8H/w4DDAMMAwwHDg8H+wPzAAAAAAAAAAAAAAAAAAAAAAAAAAAMAAwADAAMAAwADAAM/A3+DwcOAwwDDAMMAwwDDAMMAwwDDAMOAw8HDf4M/AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA/wH/g4HDAMMAAwADAAMAAwADAAMAAwADAMOBwf+A/wAAAAAAAAAAAAAAAAAAAAAAAAAAAADAAMAAwADAAMAAwPzB/sODwwHDAMMAwwDDAMMAwwDDAMMAwwHDg8H+wPzAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAD/Af+DgcMAwwDDAMP/w//DAAMAAwADAAMAw4HB/4D/AAAAAAAAAAAAAAAAAAAAAAAAAAAAD4AfwDhgMAAwADAAMAAwADAD/wP/ADAAMAAwADAAMAAwADAAMAAwADAAMAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAPzB/sODwwHDAMMAwwDDAMMAwwDDAcODwf7A/MAAwADAAMAAwwDDgcH/gP8AAAAAAAAAAAMAAwADAAMAAwADAAM/A3+DwcOAwwDDAMMAwwDDAMMAwwDDAMMAwwDDAMMAwAAAAAAAAAAAAAAAAAAAAAAAAAAAGAAYABgAAAAAAAAAeAB4ABgAGAAYABgAGAAYABgAGAAYABgAGAAYAf+B/4AAAAAAAAAAAAAAAAAAAAAAAAAAAAMAAwADAAAAAAAAAA8ADwADAAMAAwADAAMAAwADAAMAAwADAAMAAwADAAMAAwADAwMDhwH+APwAAAAAAAAAAAMAAwADAAMAAwADAAMDAwYDDAMYAzADYAPAA8ADYAMwAxgDDAMGAwMDAYMAwAAAAAAAAAAAAAAAAAAAAAAAAAAAeAB4ABgAGAAYABgAGAAYABgAGAAYABgAGAAYABgAGAAYABgAGAAYAf+B/4AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAABueH/8c85hhmGGYYZhhmGGYYZhhmGGYYZhhmGGYYZhhgAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAM/A3+DwcOAwwDDAMMAwwDDAMMAwwDDAMMAwwDDAMMAwAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA/wH/g4HDAMMAwwDDAMMAwwDDAMMAwwDDAMOBwf+A/wAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAz8Df4PBw4DDAMMAwwDDAMMAwwDDAMMAw4DDwcN/gz8DAAMAAwADAAMAAwAAAAAAAAAAAAAAAAAAAAAAAAAAAAD8wf7Dg8MBwwDDAMMAwwDDAMMAwwDDAMMBw4PB/sD8wADAAMAAwADAAMAAwAAAAAAAAAAAAAAAAAAAAAAAAAADPwN/g8HDgMMAAwADAAMAAwADAAMAAwADAAMAAwADAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAP8B/4OBwwDDAAMAA4AB/wD/gAHAAMAAwwDDgcH/gP8AAAAAAAAAAAAAAAAAAAAAAAAAAAAwADAAMAAwADAAMAAwADAD/wP/ADAAMAAwADAAMAAwADAAMAAwADgAH8APwAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAADAMMAwwDDAMMAwwDDAMMAwwDDAMMAwwDDAcODwf7A/MAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAABgBmAGYAYwDDAMMAwYGBgYDDAMMAZgBmADwAPAAYABgAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAwAPAA8ADwAPBg2GGY8ZjxmZmZmZsNjw8OBw4HDAMMAwAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAADAMGBgYGAwwDDAGYAZgA8ADwAZgBmAMMAwwGBgYGDAMAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAwDDAMMAwwDDAMMAwwDDAMMAwwDDAcODwf7A/MAAwADAAMAAwwDDgcH/gP8AAAAAAAAAAAAAAAAAAAAAAAAAAAP/w//AAMABgAMABgAMABgAMABgAMABgAMAAwAD/8P/wAAAAAAAAAAAAAAAAAAAAAAAAAAAB4APgBwAGAAYABgAGAAYABgAGAAwAeAB4AAwABgAGAAYABgAGAAYABgAHAAPgAeAAAAAAAAAAAAAAAAAAAAAABgAGAAYABgAGAAYABgAGAAYABgAGAAYABgAGAAYABgAGAAYABgAGAAYABgAGAAYAAAAAAAAAAAAAAAAAAAAAAHgAfAAOAAYABgAGAAYABgAGAAYAAwAB4AHgAwAGAAYABgAGAAYABgAGAA4AfAB4AAAAAAAAAAAAAAAAAHgY/BnOGYc5g/GB4AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA8AH4A/wD/AP8A/wB+ADwAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA4AB4AB4ABwAAAAAAGAAYADwAPABmAGYAZgDDAMMAwwGBgYGB/4H/gwDDAMMAxgBmAGYAYAAAAAAAAAAAAAAAAAcAHgB4AOAAAAAAABgAGAA8ADwAZgBmAGYAwwDDAMMBgYGBgf+B/4MAwwDDAMYAZgBmAGAAAAAAAAAAAAAAAAAYADwAZgDDAAAAAAAYABgAPAA8AGYAZgBmAMMAwwDDAYGBgYH/gf+DAMMAwwDGAGYAZgBgAAAAAAAAAAAAAAAA8MH4wx+DDwAAAAAAGAAYADwAPABmAGYAZgDDAMMAwwGBgYGB/4H/gwDDAMMAxgBmAGYAYAAAAAAAAAAAAAAAAAAAwwDDAMMAAAAAABgAGAA8ADwAZgBmAGYAwwDDAMMBgYGBgf+B/4MAwwDDAMYAZgBmAGAAAAAAAAAAAAAAAAA8AH4AwwDDAMMAfgA8ABgAPAA8AGYAZgBmAMMAwwDDAYGBgYH/gf+DAMMAwwDGAGYAZgBgAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAP/A/8GwAbADMAMwBjAGMAwwDD+YP5gwH/Af8BgwGDAYMBgwGD/YP8AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAP8B/4OBwwDDAMMAwwADAAMAAwADAAMAAwADAAMAwwDDAMOBwf+A/wAMAAwADAAYAPAA4ADgAHgAHgAHAAAAAAP/w//DAAMAAwADAAMAAwADAAP/A/8DAAMAAwADAAMAAwADAAP/w//AAAAAAAAAAAAAAAAABwAeAHgA4AAAAAAD/8P/wwADAAMAAwADAAMAAwAD/wP/AwADAAMAAwADAAMAAwAD/8P/wAAAAAAAAAAAAAAAABgAPABmAMMAAAAAA//D/8MAAwADAAMAAwADAAMAA/8D/wMAAwADAAMAAwADAAMAA//D/8AAAAAAAAAAAAAAAAAAAMMAwwDDAAAAAAP/w//DAAMAAwADAAMAAwADAAP/A/8DAAMAAwADAAMAAwADAAP/w//AAAAAAAAAAAAAAAAA4AB4AB4ABwAAAAAB/4H/gBgAGAAYABgAGAAYABgAGAAYABgAGAAYABgAGAAYABgB/4H/gAAAAAAAAAAAAAAAAAcAHgB4AOAAAAAAAf+B/4AYABgAGAAYABgAGAAYABgAGAAYABgAGAAYABgAGAAYAf+B/4AAAAAAAAAAAAAAAAAYADwAZgDDAAAAAAH/gf+AGAAYABgAGAAYABgAGAAYABgAGAAYABgAGAAYABgAGAH/gf+AAAAAAAAAAAAAAAAAAADDAMMAwwAAAAAB/4H/gBgAGAAYABgAGAAYABgAGAAYABgAGAAYABgAGAAYABgB/4H/gAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA/wD/gMHAwODAYMAwwDDAMMAz/DP8MMAwwDDAMMAwwGDA4MHA/4D/AAAAAAAAAAAAAAAAADwwfjDH4MPAAAAAAMAw4DDgMPAw8DDYMNgwzDDMMMYwxjDDMMMwwbDBsMDwwPDAcMBwwDAAAAAAAAAAAAAAAAA4AB4AB4ABwAAAAAA/wH/g4HDAMMAwwDDAMMAwwDDAMMAwwDDAMMAwwDDAMMAw4HB/4D/AAAAAAAAAAAAAAAAAAcAHgB4AOAAAAAAAP8B/4OBwwDDAMMAwwDDAMMAwwDDAMMAwwDDAMMAwwDDAMOBwf+A/wAAAAAAAAAAAAAAAAAYADwAZgDDAAAAAAD/Af+DgcMAwwDDAMMAwwDDAMMAwwDDAMMAwwDDAMMAwwDDgcH/gP8AAAAAAAAAAAAAAAAA8MH4wx+DDwAAAAAA/wH/g4HDAMMAwwDDAMMAwwDDAMMAwwDDAMMAwwDDAMMAw4HB/4D/AAAAAAAAAAAAAAAAAAAAwwDDAMMAAAAAAP8B/4OBwwDDAMMAwwDDAMMAwwDDAMMAwwDDAMMAwwDDAMOBwf+A/wAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAABgYDDAGYAPAAYABgAPABmAMMBgYAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAMAAwADA/YH/g4PDA8MGwwbDDMMMwxjDGMMYwxjDMMMww2DDYMPAw8HB/4G/AwADAAMAAAAAAAAAAOAAeAAeAAcAAAAAAwDDAMMAwwDDAMMAwwDDAMMAwwDDAMMAwwDDAMMAwwDDAMOBwf+A/wAAAAAAAAAAAAAAAAAHAB4AeADgAAAAAAMAwwDDAMMAwwDDAMMAwwDDAMMAwwDDAMMAwwDDAMMAwwDDgcH/gP8AAAAAAAAAAAAAAAAAGAA8AGYAwwAAAAADAMMAwwDDAMMAwwDDAMMAwwDDAMMAwwDDAMMAwwDDAMMAw4HB/4D/AAAAAAAAAAAAAAAAAAAAwwDDAMMAAAAAAwDDAMMAwwDDAMMAwwDDAMMAwwDDAMMAwwDDAMMAwwDDAMOBwf+A/wAAAAAAAAAAAAAAAAAHAB4AeADgAAAAAAMAwwDBgYGBgMMAwwBmAGYAPAA8ABgAGAAYABgAGAAYABgAGAAYABgAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAADAAMAAwADAAP/A/+DAcMAwwDDAMMAwwDDAMMBw/+D/wMAAwADAAMAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAPwB/gOHAwMDDwMeAzgDMAMwAzgDHwMPgwHDAMMAwwDDMMM5wx+DDwAAAAAAAAAAAAAAAAqqpVVaqqVVWqqlVVqqpVVaqqVVWqqlVVqqpVVaqqVVWqqlVVqqpVVaqqVVWqqlVVqqpVVaqqVVWqqlVVqqpVVQAAAAAAAAAAAAAAAAGAAYABgAAAAAAAAAGAAYABgAGAAYABgAGAAYABgAGAAYABgAGAAYAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAABgAGAAYABgB/4P/xxjmGGYYBhgGGAYYBhgGGAYYBhgGGGcY4//B/4AYABgAGAAYAAAAAAAAAAAAAAAAAAAAAAA/AH8A4ADAAMAAwADAAMAD/AP8AMAAwADAAMAAwADAAMDBgMP/w//AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAH4A/wHDg4GHAAYABgAf/j/8BgAGAB/4P/AGAAYABwADgYHDgP8AfgAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAwDDAMGBgYGAwwDDAGYAZgA8ADwAGAH/gf+AGAAYAf+B/4AYABgAGAAAAAAAAAAAAAAAAADDAGYAPAAYAAAAAAD/Af+DgcMAwwDDAAMAAwADgAH/AP+AAcAAwADAAMMAwwDDgcH/gP8AAAAAAAAAAAAAAAAAAAAAAAAAAAB+AP8Bw4GBgYABwADwADwA/gHDAYGBgYGBgYGAw4B/ADwADwADgAGBgYHDgP8AfgAAAAAAAAAAAAAAAADDAGYAPAAYAAAAAAAAAAAA/wH/g4HDAMMAAwADgAH/AP+AAcAAwADDAMOBwf+A/wAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAB+Af+DgccA7jx8fjznPMM8wDzAPMA8wDzDPOc8fj48dwDjgcH/gH4AAAAAAAAAAAAAAAAAAAAAAAAAAAB+AP8BgYABgP+B/4GBgYGB/4D/gAAAAAH/gf+AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAADDAYYDDAYYDDAMMAYYAwwBhgDDAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAP/w//AAMAAwADAAMAAwADAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAwDBgYDDAH4A/wHDgYGBgYGBgYGBw4D/AH4AwwGBgwDAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAH4B/4OBxwDu/Hz+PMc8wzzDPMc8/jz8PMw8xjzDPMN3AOOBwf+AfgAAAAAAAAAAAAAAAAAAAAAA/wD/AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA8AH4AwwDDAMMAwwB+ADwAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAGAAYABgAGAAYABgH/+f/4BgAGAAYABgAGAAYAAAAAAf/5//gAAAAAAAAAAAAAAAAAAAAAAAAAAAAAH4A/wGBgYGAAYABgAMADgA4AGAAwAGAAf+B/4AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAB+AP8Bw4GBgAGAAwB+AH4AAwABgYGBw4D/AH4AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAMMAZgA8ABgAAAAAAf/B/8ABgAGAAwADAAYABgAMAAwAGAAYADAAMABgAGAAwADAAf/B/8AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAwDDAMMAwwDDAMMAwwDDAMMAwwDDAMOBw4HDw8N+wzxjAAMAAwADAAMAAwAAAAAAAAAAAAAAAAAA/8H/w/zD/MP8w/zD/MP8w/zD/MH8wPzADMAMwAzADMAMwAzADMAMwAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAABgAGAAYAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAwwBmADwAGAAAAAAAAAAAA//D/8AAwAGAAwAGAAwAGAAwAGAAwAGAAwADAAP/w//AAAAAAAAAAAAAAAAAAAAAAAAAAAAYADgAeADYAZgDGAAYABgAGAAYABgAGAH/gf+AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAfgD/AcOBgYGBgYGBgYHDgP8AfgAAAAAB/4H/gAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAMMAYYAwwBhgDDAMMBhgMMBhgMMAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAB9/P/9xwGDAYMBgwGDAYMBgwGD+YP5gwGDAYMBgwGDAYMBxwD//H38AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAB54P/xzzmGGYYZhhmH+Yf5hgGGAYYBhgGGGc84//B54AAAAAAAAAAAAAAAAAAAMMAwwDDAAAAAAMAwwDBgYGBgMMAwwBmAGYAPAA8ABgAGAAYABgAGAAYABgAGAAYABgAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAMAAwADAAAAAAAAAAMAAwADAAMABgAMABgAMAAwDDAMMAw4HB/4D/AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAD/////AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAYABgAGAAYABgAGAAYABgAGAAYABgAGAAYABgAGAAYABgAGAAYABgAGAAYABgAGAAYABgAGAAYABgAGAAYABgAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAH/Af8BgAGAAYABgAGAAYABgAGAAYABgAGAAYABgAGAAYAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAD/gP+AAYABgAGAAYABgAGAAYABgAGAAYABgAGAAYABgAGAAYABgAGAAYABgAGAAYABgAGAAYABgAGAAYABgAGAAf8B/wAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAGAAYABgAGAAYABgAGAAYABgAGAAYABgAGAAYABgP+A/4AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAABgAGAAYABgAGAAYABgAGAAYABgAGAAYABgAGAAYAB/wH/AYABgAGAAYABgAGAAYABgAGAAYABgAGAAYABgAGAAYABgAGAAYABgAGAAYABgAGAAYABgAGAAYABgAGA/4D/gAGAAYABgAGAAYABgAGAAYABgAGAAYABgAGAAYABgAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAP////8BgAGAAYABgAGAAYABgAGAAYABgAGAAYABgAGAAYABgAGAAYABgAGAAYABgAGAAYABgAGAAYABgAGAAYD/////AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAYABgAGAAYABgAGAAYABgAGAAYABgAGAAYABgAGA/////wGAAYABgAGAAYABgAGAAYABgAGAAYABgAGAAYABgIiIIiKIiCIiiIgiIoiIIiKIiCIiiIgiIoiIIiKIiCIiiIgiIoiIIiKIiCIiiIgiIoiIIiKIiCIiiIgiIoiIIiL/////AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA/////wAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAP////8AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAD/////AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAD/////AAAAAP////8AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAZgBmAGYAZgBmAGYAZgBmAGYAZgBmAGYAZgBmAGYAZgBmAGYAZgBmAGYAZgBmAGYAZgBmAGYAZgBmAGYAZgBmAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAf/B/8GAAYABn8GfwZgBmAGYAZgBmAGYAZgBmAGYAZgBmAGYAZgAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAD/4P/gAGAAYP5g/mAGYAZgBmAGYAZgBmAGYAZgBmAGYAZgBmAGYAZgBmAGYAZgBmAGYAZgBmAGYAZgBmAGYAZgBn8GfwYABgAH/wf/AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAGYAZgBmAGYAZgBmAGYAZgBmAGYAZgBmAGYP5g/mAAYABg/+D/4AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAABmAGYAZgBmAGYAZgBmAGYAZgBmAGYAZgBmAGfwZ/BgAGAAZ/Bn8GYAZgBmAGYAZgBmAGYAZgBmAGYAZgBmAGYAZgBmAGYAZgBmAGYAZgBmAGYAZgBmAGYAZg/mD+YABgAGD+YP5gBmAGYAZgBmAGYAZgBmAGYAZgBmAGYAZgBmAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAP////8AAAAA/n/+fwZgBmAGYAZgBmAGYAZgBmAGYAZgBmAGYAZgBmAGYAZgBmAGYAZgBmAGYAZgBmAGYAZgBmD+f/5/AAAAAP////8AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAZgBmAGYAZgBmAGYAZgBmAGYAZgBmAGYAZg/n/+fwAAAAD+f/5/BmAGYAZgBmAGYAZgBmAGYAZgBmAGYAZgBmD/////////////////////////////////////////////////////////////////////////////////////AAAAAAAAAAAAAAAAAAAAAAAAAYADwAfgDbAZmDGMAYABgAGAAYABgAGAAYABgAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAGAAYABgAGAAYABgAGAAYAxjBmYDbAH4APAAYAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAwAGAAwAGAA//z//GAAMAAYAAwAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAADAAGAAMAAY//z//AAYADAAYADAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAOAAeAAeAAcAAAAAAAAAAAD/Af+DgcMAwADAAMAAwP/B/8OAwwDDAMMBw4PB/sD8wAAAAAAAAAAAAAAAAAAAAAAHAB4AeADgAAAAAAAAAAAA/wH/g4HDAMAAwADAAMD/wf/DgMMAwwDDAcODwf7A/MAAAAAAAAAAAAAAAAAAAAAAGAA8AGYAwwAAAAAAAAAAAP8B/4OBwwDAAMAAwADA/8H/w4DDAMMAwwHDg8H+wPzAAAAAAAAAAAAAAAAAAAAAAPDB+MMfgw8AAAAAAAAAAAD/Af+DgcMAwADAAMAAwP/B/8OAwwDDAMMBw4PB/sD8wAAAAAAAAAAAAAAAAAAAAAAAAMMAwwDDAAAAAAAAAAAA/wH/g4HDAMAAwADAAMD/wf/DgMMAwwDDAcODwf7A/MAAAAAAAAAAAAAAAAA8AH4AwwDDAMMAwwB+ADwAAAAAAP8B/4OBwwDAAMAAwADA/8H/w4DDAMMAwwHDg8H+wPzAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAHng//HPOYYYBhgGGAYYf/j/+cYBhgGGAYYZzzj/8HngAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA/wH/g4HDAMMAAwADAAMAAwADAAMAAwADAMOBwf+A/wAMAAwADAAYAPAA4AAAAAAA4AB4AB4ABwAAAAAAAAAAAP8B/4OBwwDDAMMAw//D/8MAAwADAAMAAwDDgcH/gP8AAAAAAAAAAAAAAAAAAAAAAAcAHgB4AOAAAAAAAAAAAAD/Af+DgcMAwwDDAMP/w//DAAMAAwADAAMAw4HB/4D/AAAAAAAAAAAAAAAAAAAAAAAYADwAZgDDAAAAAAAAAAAA/wH/g4HDAMMAwwDD/8P/wwADAAMAAwADAMOBwf+A/wAAAAAAAAAAAAAAAAAAAAAAAADDAMMAwwAAAAAAAAAAAP8B/4OBwwDDAMMAw//D/8MAAwADAAMAAwDDgcH/gP8AAAAAAAAAAAAAAAAAAAAAAOAAeAAeAAcAAAAAAAAAAAB4AHgAGAAYABgAGAAYABgAGAAYABgAGAAYABgB/4H/gAAAAAAAAAAAAAAAAAAAAAAHAB4AeADgAAAAAAAAAAAAeAB4ABgAGAAYABgAGAAYABgAGAAYABgAGAAYAf+B/4AAAAAAAAAAAAAAAAAAAAAAGAA8AGYAwwAAAAAAAAAAAHgAeAAYABgAGAAYABgAGAAYABgAGAAYABgAGAH/gf+AAAAAAAAAAAAAAAAAAAAAAAAAwwDDAMMAAAAAAAAAAAB4AHgAGAAYABgAGAAYABgAGAAYABgAGAAYABgB/4H/gAAAAAAAAAAAAAAAAAAAAAAAAAADwcPjgH4AfAHGA4MAAYAAwPzB/sODwwHDAMMAwwDDAMMAwwDDAMOBwf+A/wAAAAAAAAAAAAAAAAAAAAAA8MH4wx+DDwAAAAAAAAAAAz8Df4PBw4DDAMMAwwDDAMMAwwDDAMMAwwDDAMMAwwDAAAAAAAAAAAAAAAAAAAAAAOAAeAAeAAcAAAAAAAAAAAD/Af+DgcMAwwDDAMMAwwDDAMMAwwDDAMMAw4HB/4D/AAAAAAAAAAAAAAAAAAAAAAAHAB4AeADgAAAAAAAAAAAA/wH/g4HDAMMAwwDDAMMAwwDDAMMAwwDDAMOBwf+A/wAAAAAAAAAAAAAAAAAAAAAAGAA8AGYAwwAAAAAAAAAAAP8B/4OBwwDDAMMAwwDDAMMAwwDDAMMAwwDDgcH/gP8AAAAAAAAAAAAAAAAAAAAAAPDB+MMfgw8AAAAAAAAAAAD/Af+DgcMAwwDDAMMAwwDDAMMAwwDDAMMAw4HB/4D/AAAAAAAAAAAAAAAAAAAAAAAAAMMAwwDDAAAAAAAAAAAA/wH/g4HDAMMAwwDDAMMAwwDDAMMAwwDDAMOBwf+A/wAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAABgAGAAYAAAAAAAAAAAAAAH/gf+AAAAAAAAAAAAAABgAGAAYAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAwADAAMD9gf+Dh4MGwwbDDMMMwxjDGMMwwzDDYMNgwcHB/4G/AwADAAMAAAAAAAAAAAAAAADgAHgAHgAHAAAAAAAAAAADAMMAwwDDAMMAwwDDAMMAwwDDAMMAwwDDAcODwf7A/MAAAAAAAAAAAAAAAAAAAAAABwAeAHgA4AAAAAAAAAAAAwDDAMMAwwDDAMMAwwDDAMMAwwDDAMMAwwHDg8H+wPzAAAAAAAAAAAAAAAAAAAAAABgAPABmAMMAAAAAAAAAAAMAwwDDAMMAwwDDAMMAwwDDAMMAwwDDAMMBw4PB/sD8wAAAAAAAAAAAAAAAAAAAAAAAAMMAwwDDAAAAAAAAAAADAMMAwwDDAMMAwwDDAMMAwwDDAMMAwwDDAcODwf7A/MAAAAAAAAAAAAAAAAAAAAAABwAeAHgA4AAAAAAAAAAAAwDDAMMAwwDDAMMAwwDDAMMAwwDDAcODwf7A/MAAwADAAMAAwwDDgcH/gP8AAAAAAAAAAAMAAwADAAMAAwADAAM/A3+DwcOAwwDDAMMAwwDDAMMAwwDDAMOAw8HDf4M/AwADAAMAAwADAAMAAAAAAAAAAMMAwwDDAAAAAAAAAAADAMMAwwDDAMMAwwDDAMMAwwDDAMMBw4PB/sD8wADAAMAAwADDAMOBwf+A/w77+9/8+A/+KJoP/iiaT/4oml/+KWoOKWrOKWruKXvOKXvuKsm+KIjv/il4bimabirKXirKf/wrz/wr3/wr7/wqb/wqj/wrj/xpL/4oCg/+KAof/igLD/4oSi/+KApv/igLn/4oC6/+KAnOKAn+KAtv/igJ3Kusudy67igLP/4oCe/+K5gv/iuYHLjv/Env/En//EsP/Esf/Fnv/Fn/8gwqDigIDigIHigILigIPigITigIXigIbigIfigIjigInigIrigK/igZ//If8i/yP/JP8l/yb/J8K0yrnKvMuK4oCZ4oCy/yj/Kf8q4oGO4oiX/yv/LMuP4oCa/y3CreKAkOKAkeKAkuKAk+KBg+KIkv8u4oCk/y/igYTiiJX/MP8x/zL/M/80/zX/Nv83/zj/Of864oi2/zv/PP894rmA/z7/P/9A/0H/Qv9D/0T/Rf9G/0f/SP9J/0r/S+KEqv9M/03/Tv9P/1D/Uf9S/1P/VP9V/1b/V/9Y/1n/Wv9b/1zip7X/Xf9ey4TLhuKMg/9f/2DKu8q9y4vigJjigJvigLX/Yf9i/2P/ZP9l/2b/Z/9o/2n/av9r/2z/bf9u/2//cP9x/3L/c/90/3X/dv93/3j/ef96/3v/fOKIo/99/37LnP/igKLiiJnil4//w4D/w4H/w4L/w4P/w4T/w4XihKv/w4b/w4f/w4j/w4n/w4r/w4v/w4z/w43/w47/w4//w5DEkP/Dkf/Dkv/Dk//DlP/Dlf/Dlv/Dl//DmP/Dmf/Dmv/Dm//DnP/Dnf/Dnv/Dn//ilpL/wqH/wqL/wqP/4oKs/8Kl/8Wg/8Kn/8Wh/8Kp/8Kq/8Kr/8Ks/8Kk/8Ku/8Kvy4n/wrDLmv/Csf/Csv/Cs//Fvf/Ctc68/8K2/8K34oCn4ouF4rix/8W+/8K5/8K6/8K7/8WS/8WT/8W4/8K//+KUgOKAlOKAleKOr//ilIL/4pSM4pWt/+KUkOKVrv/ilJTilbD/4pSY4pWv/+KUnP/ilKT/4pSs/+KUtP/ilLz/4paR/+KOuuKAvv/ijrv/4o68/+KOvf/ilZDilIH/4pWR4pSD/+KVlOKUj//ilZfilJP/4pWa4pSX/+KVneKUm//ilaDilKP/4pWj4pSr/+KVpuKUs//ilanilLv/4pWs4pWL/+KWiP/ihpH/4oaT/+KGkP/ihpL/w6D/w6H/w6L/w6P/w6T/w6X/w6b/w6f/w6j/w6n/w6r/w6v/w6z/w63/w67/w6//w7D/w7H/w7L/w7P/w7T/w7X/w7b/w7f/w7j/w7n/w7r/w7v/w7z/w73/w77/w7//";
                fixed (char* ptr = ZapLight)
                {
                    return ptr;
                }
            }
        }
        public static byte* Framebuffer;
        public static int Scanline;
        public static byte* FontData;
        public static ushort* UnicodeTable = null; // Optional
        private static bool Initialized;
        public static ushort Magic = 0x0436;
        public static uint PSFFontMagic = 0x864ab572;

        public struct PSF_Header
        {
            public ushort Magic;
            public byte Mode;
            public byte CharSize;
        }

        public struct PSF_Font
        {
            public uint Magic; // PSF magic number
            public uint Version; // Always 0
            public uint HeaderSize; // Offset of the bitmaps in the file
            public uint Flags; // 0 if no unicode table
            public uint NumGlyph; // Number of glyphs in the font
            public uint BytesPerGlyph; // size of each glyph
            public uint Height; // Height in pixels
            public uint Width; // Width in pixels
        }

        // You can initialize things here
        public static void Init(byte* fb, int scanline, byte* fontData)
        {
            Framebuffer = fb;
            Scanline = scanline;
            FontData = fontData;
        }

        // Example allocator use
        public static void LoadFont(byte* fontFile, uint Length)
        {
            FontData = (byte*)MemoryOp.Alloc(Length);
            for (int i = 0; i < Length; i++)
                FontData[i] = fontFile[i];
        }

        public static void PutString(string str, int x, int y, uint fg, uint bg)
        {
            fixed (char* ptr = str)
            {
                for (int i = 0; i < str.Length; i++)
                {
                    PutChar(ptr[i], x + (i * 16), y, fg, bg);
                }
            }
        }

        public static void PutChar(
            ushort c, int cx, int cy,
            uint fg, uint bg)
        {

            if (!Initialized)
            {
                byte* fontData = Base64.Decode(Default.GetUnmanagedFontData(), (uint)Default.Size);

                Init((byte*)Graphics.Canvas.Address, (int)Graphics.Canvas.Pitch, fontData);

                Initialized = true;
            }

            PSF_Font* font = (PSF_Font*)FontData;
            int bytesPerLine = ((int)font->Width + 7) / 8;

            // Ensure the character is within the ASCII range (0-127)
            c = (ushort)(c & 0x7F);

            byte* glyph = FontData
                + font->HeaderSize
                + ((c < font->NumGlyph ? c : 0) * font->BytesPerGlyph);

            int offs = (int)((cy * font->Height * Scanline) +
                       (cx * (int)font->Width * sizeof(uint)));

            for (int y = 0; y < font->Height; y++)
            {
                int line = offs;

                int rowData = 0;
                for (int b = 0; b < bytesPerLine; b++)
                {
                    rowData |= glyph[b] << (8 * (bytesPerLine - 1 - b));
                }

                int mask = 1 << ((int)font->Width - 1);

                for (int x = 0; x < font->Width; x++)
                {


                    if ((rowData & mask) != 0)
                    {
                        Graphics.Canvas.DrawPixel(fg, cx + x, cy + y);
                    }
                    else if ((bg & 0xFF000000) != 0)
                    {
                        Graphics.Canvas.DrawPixel(bg, cx + x, cy + y);
                    }

                    mask >>= 1;
                }

                glyph += bytesPerLine;
                offs += Scanline;
            }
        }
    }
}
