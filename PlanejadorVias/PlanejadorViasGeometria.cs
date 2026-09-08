using System;
using Autodesk.AutoCAD.Geometry;

namespace AutomacoesCivil3D
{
    /// <summary>
    /// Resultado da concordância (fillet) em um vértice de polyline.
    /// </summary>
    public struct ConcordanciaVertice
    {
        public bool Possivel;
        public Point2d TangenteEntrada;
        public Point2d TangenteSaida;
        public double Bulge;
    }

    /// <summary>
    /// Resultado do fillet de canto entre duas linhas de limite em uma junção.
    /// </summary>
    public struct FilletQuadrante
    {
        public bool Possivel;
        public Point2d Centro;
        public Point2d TangenteA;
        public Point2d TangenteB;
        public Point2d Quina;
        public double AnguloInicial;
        public double AnguloFinal;
        public double Raio;
    }

    /// <summary>
    /// Geometria plana pura do Planejador de Vias (sem acesso a entidades do AutoCAD).
    /// Convenção: normal esquerda de um vetor v é Rot90(v) = (-v.Y, v.X).
    /// </summary>
    public static class PlanejadorViasGeometria
    {
        public const double Tolerancia = 1e-8;

        public static Vector2d NormalEsquerda(Vector2d v)
        {
            return new Vector2d(-v.Y, v.X);
        }

        public static double Cruzado(Vector2d a, Vector2d b)
        {
            return a.X * b.Y - a.Y * b.X;
        }

        /// <summary>
        /// Concordância circular no vértice B do caminho A-B-C.
        /// O raio é reduzido automaticamente quando os segmentos são curtos demais
        /// (no máximo <paramref name="fracaoMaxima"/> de cada segmento vira tangente).
        /// </summary>
        public static ConcordanciaVertice ConcordarVertice(Point2d a, Point2d b, Point2d c, double raio, double fracaoMaxima = 0.45)
        {
            ConcordanciaVertice resultado = new ConcordanciaVertice { Possivel = false };

            Vector2d seg1 = b - a;
            Vector2d seg2 = c - b;
            double len1 = seg1.Length;
            double len2 = seg2.Length;
            if (raio <= Tolerancia || len1 < Tolerancia || len2 < Tolerancia)
            {
                return resultado;
            }

            Vector2d u1 = new Vector2d(seg1.X / len1, seg1.Y / len1);
            Vector2d u2 = new Vector2d(seg2.X / len2, seg2.Y / len2);

            double cruz = Cruzado(u1, u2);
            double ponto = u1.DotProduct(u2);

            // Colinear (sem deflexão) ou reversão total: não concorda.
            if (Math.Abs(cruz) < 1e-9 || ponto < -0.9999)
            {
                return resultado;
            }

            // Ângulo interno entre os segmentos no vértice (entre B->A e B->C).
            double cosAlfa = Math.Max(-1.0, Math.Min(1.0, (-u1).DotProduct(u2)));
            double alfa = Math.Acos(cosAlfa);
            if (alfa < 1e-6 || alfa > Math.PI - 1e-6)
            {
                return resultado;
            }

            double tangente = raio / Math.Tan(alfa / 2.0);
            double tangenteMaxima = fracaoMaxima * Math.Min(len1, len2);
            if (tangente > tangenteMaxima)
            {
                tangente = tangenteMaxima;
                if (tangente < Tolerancia)
                {
                    return resultado;
                }
            }

            double varredura = Math.PI - alfa;
            double bulge = Math.Tan(varredura / 4.0) * Math.Sign(cruz);

            resultado.Possivel = true;
            resultado.TangenteEntrada = b - u1 * tangente;
            resultado.TangenteSaida = b + u2 * tangente;
            resultado.Bulge = bulge;
            return resultado;
        }

        /// <summary>
        /// Interseção entre duas retas (ponto + direção). Retorna false se quase paralelas.
        /// </summary>
        public static bool InterseccaoRetas(Point2d p1, Vector2d u1, Point2d p2, Vector2d u2, out Point2d intersecao)
        {
            intersecao = Point2d.Origin;
            double det = Cruzado(u1, u2);
            if (Math.Abs(det) < 1e-9)
            {
                return false;
            }

            Vector2d delta = p2 - p1;
            double t = Cruzado(delta, u2) / det;
            intersecao = p1 + u1 * t;
            return true;
        }

        /// <summary>
        /// Resolve o ponto X tal que (X-P)·nA = dA e (X-P)·nB = dB (base oblíqua).
        /// nA e nB devem ser unitários. Retorna false quando as vias são quase paralelas.
        /// </summary>
        public static bool ResolverBaseObliqua(Point2d p, Vector2d nA, Vector2d nB, double dA, double dB, out Point2d ponto)
        {
            ponto = Point2d.Origin;
            double c = nA.DotProduct(nB);
            double det = 1.0 - c * c;
            if (Math.Abs(det) < 1e-6)
            {
                return false;
            }

            double a = (dA - c * dB) / det;
            double b = (dB - c * dA) / det;
            ponto = p + nA * a + nB * b;
            return true;
        }

        /// <summary>
        /// Fillet tangente às linhas de limite de duas vias em um quadrante da junção.
        /// P: interseção dos eixos; nA/nB: normais esquerdas unitárias dos eixos;
        /// offsetA/offsetB: offsets assinados das linhas de limite (sinal = lado do quadrante);
        /// raio: raio de concordância do meio-fio.
        /// </summary>
        public static FilletQuadrante FilletDeQuadrante(
            Point2d p,
            Vector2d nA,
            Vector2d nB,
            double offsetA,
            double offsetB,
            double raio)
        {
            FilletQuadrante resultado = new FilletQuadrante { Possivel = false, Raio = raio };

            double sinalA = Math.Sign(offsetA);
            double sinalB = Math.Sign(offsetB);
            if (Math.Abs(sinalA) < 0.5 || Math.Abs(sinalB) < 0.5 || raio <= Tolerancia)
            {
                return resultado;
            }

            Point2d centro;
            if (!ResolverBaseObliqua(p, nA, nB, offsetA + sinalA * raio, offsetB + sinalB * raio, out centro))
            {
                return resultado;
            }

            Point2d quina;
            if (!ResolverBaseObliqua(p, nA, nB, offsetA, offsetB, out quina))
            {
                return resultado;
            }

            // Tangentes = projeção do centro sobre cada linha de limite.
            Point2d tangenteA = centro - nA * (sinalA * raio);
            Point2d tangenteB = centro - nB * (sinalB * raio);

            double angA = AnguloDe(tangenteA - centro);
            double angB = AnguloDe(tangenteB - centro);
            double angQuina = AnguloDe(quina - centro);

            // Escolhe o sentido anti-horário que contém a direção da quina
            // (o arco deve dar a volta pelo lado do canto).
            double inicio;
            double fim;
            if (VarreduraCcw(angA, angQuina) <= VarreduraCcw(angA, angB) + 1e-9)
            {
                inicio = angA;
                fim = angB;
            }
            else
            {
                inicio = angB;
                fim = angA;
            }

            resultado.Possivel = true;
            resultado.Centro = centro;
            resultado.TangenteA = tangenteA;
            resultado.TangenteB = tangenteB;
            resultado.Quina = quina;
            resultado.AnguloInicial = inicio;
            resultado.AnguloFinal = fim;
            return resultado;
        }

        /// <summary>
        /// Arco concêntrico ao retorno de meio-fio: mesmo centro do fillet da borda,
        /// raio reduzido pela profundidade do anel (banda de largura constante).
        /// </summary>
        public static FilletQuadrante FilletConcentrico(
            Point2d centro,
            double raio,
            Vector2d nA,
            double sinalA,
            Vector2d nB,
            double sinalB,
            Point2d quina)
        {
            FilletQuadrante resultado = new FilletQuadrante
            {
                Possivel = raio > Tolerancia,
                Raio = raio,
                Centro = centro,
                Quina = quina
            };

            if (!resultado.Possivel)
            {
                return resultado;
            }

            resultado.TangenteA = centro - nA * (sinalA * raio);
            resultado.TangenteB = centro - nB * (sinalB * raio);

            double angA = AnguloDe(resultado.TangenteA - centro);
            double angB = AnguloDe(resultado.TangenteB - centro);
            double angQuina = AnguloDe(quina - centro);

            if (VarreduraCcw(angA, angQuina) <= VarreduraCcw(angA, angB) + 1e-9)
            {
                resultado.AnguloInicial = angA;
                resultado.AnguloFinal = angB;
            }
            else
            {
                resultado.AnguloInicial = angB;
                resultado.AnguloFinal = angA;
            }

            return resultado;
        }

        public static double AnguloDe(Vector2d v)
        {
            return Math.Atan2(v.Y, v.X);
        }

        /// <summary>Varredura anti-horária (0..2π) do ângulo "de" até o ângulo "ate".</summary>
        public static double VarreduraCcw(double de, double ate)
        {
            double d = ate - de;
            while (d < 0.0)
            {
                d += 2.0 * Math.PI;
            }

            while (d >= 2.0 * Math.PI)
            {
                d -= 2.0 * Math.PI;
            }

            return d;
        }
    }
}
