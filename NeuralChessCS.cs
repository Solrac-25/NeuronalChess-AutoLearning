using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;

namespace NeuralChessCS
{
    public enum PieceType { None, Pawn, Knight, Bishop, Rook, Queen, King }
    public enum Color { White, Black }

    public struct Piece
    {
        public PieceType Type;
        public Color Color;
        public static Piece Empty => new Piece { Type = PieceType.None };
        public char ToChar()
        {
            char c = '.';
            switch (Type)
            {
                case PieceType.Pawn: c = 'p'; break;
                case PieceType.Knight: c = 'n'; break;
                case PieceType.Bishop: c = 'b'; break;
                case PieceType.Rook: c = 'r'; break;
                case PieceType.Queen: c = 'q'; break;
                case PieceType.King: c = 'k'; break;
            }
            return Color == Color.White ? char.ToUpper(c) : c;
        }
    }

    public struct Move
    {
        public int From, To;
        public PieceType Promotion;
        public bool IsCapture, IsCastle, IsEnPassant, IsDoublePush;
        public string UCI
        {
            get
            {
                char promo = ' ';
                if (Promotion != PieceType.None)
                {
                    switch (Promotion)
                    {
                        case PieceType.Queen: promo = 'q'; break;
                        case PieceType.Rook: promo = 'r'; break;
                        case PieceType.Bishop: promo = 'b'; break;
                        case PieceType.Knight: promo = 'n'; break;
                    }
                }
                string u = string.Format("{0}{1}{2}{3}",
                    (char)('a' + (From % 8)),
                    (char)('1' + (From / 8)),
                    (char)('a' + (To % 8)),
                    (char)('1' + (To / 8)));
                if (promo != ' ') u += promo;
                return u;
            }
        }
    }

    public class Board
    {
        public Piece[] Squares = new Piece[64];
        public Color SideToMove = Color.White;
        public int HalfmoveClock = 0;
        public int FullmoveNumber = 1;
        public bool WhiteCastleKingside = true, WhiteCastleQueenside = true;
        public bool BlackCastleKingside = true, BlackCastleQueenside = true;
        public int? EnPassantSquare = null;
        public List<string> PositionHistory = new List<string>();

        public Board Clone()
        {
            var b = new Board();
            b.Squares = Squares.ToArray();
            b.SideToMove = SideToMove;
            b.HalfmoveClock = HalfmoveClock;
            b.FullmoveNumber = FullmoveNumber;
            b.WhiteCastleKingside = WhiteCastleKingside;
            b.WhiteCastleQueenside = WhiteCastleQueenside;
            b.BlackCastleKingside = BlackCastleKingside;
            b.BlackCastleQueenside = BlackCastleQueenside;
            b.EnPassantSquare = EnPassantSquare;
            b.PositionHistory = new List<string>(PositionHistory);
            return b;
        }

        public string ToFenKey()
        {
            var sb = new StringBuilder();
            for (int i = 0; i < 64; i++) sb.Append(Squares[i].ToChar());
            sb.Append(SideToMove == Color.White ? 'w' : 'b');
            sb.Append(WhiteCastleKingside ? 'K' : '-');
            sb.Append(WhiteCastleQueenside ? 'Q' : '-');
            sb.Append(BlackCastleKingside ? 'k' : '-');
            sb.Append(BlackCastleQueenside ? 'q' : '-');
            sb.Append(EnPassantSquare.HasValue ? EnPassantSquare.Value.ToString() : "-");
            return sb.ToString();
        }

        public void PushHistory() => PositionHistory.Add(ToFenKey());

        public void MakeMove(Move m)
        {
            var piece = Squares[m.From];
            Squares[m.To] = piece;
            Squares[m.From] = Piece.Empty;

            if (m.IsEnPassant)
            {
                int capSq = SideToMove == Color.White ? m.To - 8 : m.To + 8;
                Squares[capSq] = Piece.Empty;
            }
            if (m.Promotion != PieceType.None)
                Squares[m.To] = new Piece { Type = m.Promotion, Color = SideToMove };

            if (m.IsCastle)
            {
                if (m.To == 6) { Squares[5] = Squares[7]; Squares[7] = Piece.Empty; }
                else if (m.To == 2) { Squares[3] = Squares[0]; Squares[0] = Piece.Empty; }
                else if (m.To == 62) { Squares[61] = Squares[63]; Squares[63] = Piece.Empty; }
                else if (m.To == 58) { Squares[59] = Squares[56]; Squares[56] = Piece.Empty; }
            }

            if (piece.Type == PieceType.King)
            {
                if (SideToMove == Color.White) { WhiteCastleKingside = false; WhiteCastleQueenside = false; }
                else { BlackCastleKingside = false; BlackCastleQueenside = false; }
            }
            if (piece.Type == PieceType.Rook)
            {
                if (m.From == 0) WhiteCastleQueenside = false;
                if (m.From == 7) WhiteCastleKingside = false;
                if (m.From == 56) BlackCastleQueenside = false;
                if (m.From == 63) BlackCastleKingside = false;
            }

            EnPassantSquare = null;
            if (m.IsDoublePush)
                EnPassantSquare = SideToMove == Color.White ? m.From + 8 : m.From - 8;

            if (piece.Type == PieceType.Pawn || m.IsCapture) HalfmoveClock = 0; else HalfmoveClock++;
            if (SideToMove == Color.Black) FullmoveNumber++;
            SideToMove = SideToMove == Color.White ? Color.Black : Color.White;
        }
    }

    public static class MoveGenerator
    {
        static readonly int[] KnightOffsets = { -17, -15, -10, -6, 6, 10, 15, 17 };
        static readonly int[] KingOffsets = { -9, -8, -7, -1, 1, 7, 8, 9 };

        static bool OnBoard(int sq) => sq >= 0 && sq < 64;
        static int Rank(int sq) => sq / 8;
        static int File(int sq) => sq % 8;

        public static List<Move> GenerateLegalMoves(Board board)
        {
            var pseudo = GeneratePseudoLegal(board);
            var legal = new List<Move>();
            foreach (var m in pseudo)
            {
                var next = board.Clone();
                next.MakeMove(m);
                if (!IsInCheck(next, board.SideToMove)) legal.Add(m);
            }
            return legal;
        }

        public static List<Move> GeneratePseudoLegal(Board board)
        {
            var moves = new List<Move>();
            Color us = board.SideToMove;
            for (int sq = 0; sq < 64; sq++)
            {
                var p = board.Squares[sq];
                if (p.Type == PieceType.None || p.Color != us) continue;
                switch (p.Type)
                {
                    case PieceType.Pawn: GenPawn(sq, us, board, moves); break;
                    case PieceType.Knight: GenKnight(sq, us, board, moves); break;
                    case PieceType.Bishop: GenSliding(sq, us, board, new int[] { -9, -7, 7, 9 }, moves); break;
                    case PieceType.Rook: GenSliding(sq, us, board, new int[] { -8, -1, 1, 8 }, moves); break;
                    case PieceType.Queen: GenSliding(sq, us, board, new int[] { -9, -8, -7, -1, 1, 7, 8, 9 }, moves); break;
                    case PieceType.King: GenKing(sq, us, board, moves); break;
                }
            }
            return moves;
        }

        static void GenPawn(int sq, Color c, Board b, List<Move> moves)
        {
            int dir = c == Color.White ? 8 : -8;
            int startRank = c == Color.White ? 1 : 6;
            int promoRank = c == Color.White ? 7 : 0;
            int fr = Rank(sq), fc = File(sq);

            int to = sq + dir;
            if (OnBoard(to) && b.Squares[to].Type == PieceType.None)
            {
                if (Rank(to) == promoRank) AddPromos(sq, to, false, moves);
                else moves.Add(new Move { From = sq, To = to });

                if (fr == startRank && b.Squares[to + dir].Type == PieceType.None)
                    moves.Add(new Move { From = sq, To = to + dir, IsDoublePush = true });
            }
            foreach (int off in new int[] { -1, 1 })
            {
                if (fc + off < 0 || fc + off > 7) continue;
                int cap = sq + dir + off;
                if (!OnBoard(cap)) continue;
                if (b.Squares[cap].Type != PieceType.None && b.Squares[cap].Color != c)
                {
                    if (Rank(cap) == promoRank) AddPromos(sq, cap, true, moves);
                    else moves.Add(new Move { From = sq, To = cap, IsCapture = true });
                }
                if (b.EnPassantSquare.HasValue && cap == b.EnPassantSquare.Value)
                    moves.Add(new Move { From = sq, To = cap, IsCapture = true, IsEnPassant = true });
            }
        }

        static void AddPromos(int from, int to, bool cap, List<Move> moves)
        {
            moves.Add(new Move { From = from, To = to, IsCapture = cap, Promotion = PieceType.Queen });
            moves.Add(new Move { From = from, To = to, IsCapture = cap, Promotion = PieceType.Rook });
            moves.Add(new Move { From = from, To = to, IsCapture = cap, Promotion = PieceType.Bishop });
            moves.Add(new Move { From = from, To = to, IsCapture = cap, Promotion = PieceType.Knight });
        }

        static void GenKnight(int sq, Color c, Board b, List<Move> moves)
        {
            int r = Rank(sq), f = File(sq);
            foreach (int off in KnightOffsets)
            {
                int to = sq + off;
                if (!OnBoard(to)) continue;
                int tr = Rank(to), tf = File(to);
                if (Math.Abs(tr - r) > 2 || Math.Abs(tf - f) > 2) continue;
                if (b.Squares[to].Type == PieceType.None) moves.Add(new Move { From = sq, To = to });
                else if (b.Squares[to].Color != c) moves.Add(new Move { From = sq, To = to, IsCapture = true });
            }
        }

        static void GenSliding(int sq, Color c, Board b, int[] dirs, List<Move> moves)
        {
            foreach (int d in dirs)
            {
                int to = sq + d;
                while (OnBoard(to))
                {
                    if (Math.Abs(File(to) - File(to - d)) > 1 && Math.Abs(d) != 8) break;
                    if (b.Squares[to].Type == PieceType.None) moves.Add(new Move { From = sq, To = to });
                    else
                    {
                        if (b.Squares[to].Color != c) moves.Add(new Move { From = sq, To = to, IsCapture = true });
                        break;
                    }
                    to += d;
                }
            }
        }

        static void GenKing(int sq, Color c, Board b, List<Move> moves)
        {
            int r = Rank(sq), f = File(sq);
            foreach (int off in KingOffsets)
            {
                int to = sq + off;
                if (!OnBoard(to)) continue;
                int tr = Rank(to), tf = File(to);
                if (Math.Abs(tr - r) > 1 || Math.Abs(tf - f) > 1) continue;
                if (b.Squares[to].Type == PieceType.None) moves.Add(new Move { From = sq, To = to });
                else if (b.Squares[to].Color != c) moves.Add(new Move { From = sq, To = to, IsCapture = true });
            }
            if (c == Color.White)
            {
                if (b.WhiteCastleKingside && b.Squares[5].Type == PieceType.None && b.Squares[6].Type == PieceType.None)
                    if (!IsAttacked(b, 4, Color.Black) && !IsAttacked(b, 5, Color.Black))
                        moves.Add(new Move { From = 4, To = 6, IsCastle = true });
                if (b.WhiteCastleQueenside && b.Squares[3].Type == PieceType.None && b.Squares[2].Type == PieceType.None && b.Squares[1].Type == PieceType.None)
                    if (!IsAttacked(b, 4, Color.Black) && !IsAttacked(b, 3, Color.Black))
                        moves.Add(new Move { From = 4, To = 2, IsCastle = true });
            }
            else
            {
                if (b.BlackCastleKingside && b.Squares[61].Type == PieceType.None && b.Squares[62].Type == PieceType.None)
                    if (!IsAttacked(b, 60, Color.White) && !IsAttacked(b, 61, Color.White))
                        moves.Add(new Move { From = 60, To = 62, IsCastle = true });
                if (b.BlackCastleQueenside && b.Squares[59].Type == PieceType.None && b.Squares[58].Type == PieceType.None && b.Squares[57].Type == PieceType.None)
                    if (!IsAttacked(b, 60, Color.White) && !IsAttacked(b, 59, Color.White))
                        moves.Add(new Move { From = 60, To = 58, IsCastle = true });
            }
        }

        public static bool IsInCheck(Board b, Color c)
        {
            int kingSq = -1;
            for (int i = 0; i < 64; i++) if (b.Squares[i].Type == PieceType.King && b.Squares[i].Color == c) { kingSq = i; break; }
            return IsAttacked(b, kingSq, c == Color.White ? Color.Black : Color.White);
        }

        static bool IsAttacked(Board b, int sq, Color by)
        {
            int pd = by == Color.White ? 8 : -8;
            int sqFile = File(sq);
            foreach (int off in new int[] { pd - 1, pd + 1 })
            {
                int s = sq - off;
                if (!OnBoard(s)) continue;
                if (Math.Abs(File(s) - sqFile) != 1) continue;
                if (b.Squares[s].Type == PieceType.Pawn && b.Squares[s].Color == by) return true;
            }
            int r = Rank(sq), f = File(sq);
            foreach (int off in KnightOffsets)
            {
                int to = sq + off;
                if (!OnBoard(to)) continue;
                if (Math.Abs(Rank(to) - r) > 2 || Math.Abs(File(to) - f) > 2) continue;
                if (b.Squares[to].Type == PieceType.Knight && b.Squares[to].Color == by) return true;
            }
            foreach (int off in KingOffsets)
            {
                int to = sq + off;
                if (!OnBoard(to)) continue;
                if (Math.Abs(Rank(to) - r) > 1 || Math.Abs(File(to) - f) > 1) continue;
                if (b.Squares[to].Type == PieceType.King && b.Squares[to].Color == by) return true;
            }
            foreach (int d in new int[] { -8, -1, 1, 8 })
            {
                int to = sq + d;
                while (OnBoard(to))
                {
                    if (Math.Abs(File(to) - File(to - d)) > 1 && Math.Abs(d) != 8) break;
                    var p = b.Squares[to];
                    if (p.Type != PieceType.None)
                    {
                        if (p.Color == by && (p.Type == PieceType.Rook || p.Type == PieceType.Queen)) return true;
                        break;
                    }
                    to += d;
                }
            }
            foreach (int d in new int[] { -9, -7, 7, 9 })
            {
                int to = sq + d;
                while (OnBoard(to))
                {
                    if (Math.Abs(File(to) - File(to - d)) > 1) break;
                    var p = b.Squares[to];
                    if (p.Type != PieceType.None)
                    {
                        if (p.Color == by && (p.Type == PieceType.Bishop || p.Type == PieceType.Queen)) return true;
                        break;
                    }
                    to += d;
                }
            }
            return false;
        }

        public static string GameResult(Board b, List<Move> legalMoves)
        {
            if (legalMoves.Count == 0)
                return IsInCheck(b, b.SideToMove) ? (b.SideToMove == Color.White ? "0-1" : "1-0") : "1/2-1/2";
            if (b.HalfmoveClock >= 100) return "1/2-1/2";
            if (InsufficientMaterial(b)) return "1/2-1/2";
            var current = b.ToFenKey();
            int reps = 0;
            foreach (var h in b.PositionHistory) if (h == current) reps++;
            if (reps >= 2) return "1/2-1/2";
            return null;
        }

        public static bool InsufficientMaterial(Board b)
        {
            int wN = 0, wB = 0, bN = 0, bB = 0, other = 0;
            int wBsq = -1, bBsq = -1;
            for (int i = 0; i < 64; i++)
            {
                var p = b.Squares[i];
                if (p.Type == PieceType.None) continue;
                if (p.Type == PieceType.King) continue;
                if (p.Type == PieceType.Knight) { if (p.Color == Color.White) wN++; else bN++; }
                else if (p.Type == PieceType.Bishop) { if (p.Color == Color.White) { wB++; wBsq = i; } else { bB++; bBsq = i; } }
                else other++;
            }
            if (other > 0) return false;
            if (wN == 0 && bN == 0 && wB == 0 && bB == 0) return true;
            if (wN + wB == 0 && bN + bB == 1) return true;
            if (bN + bB == 0 && wN + wB == 1) return true;
            if (wN == 0 && bN == 0 && wB == 1 && bB == 1)
                return ((wBsq / 8 + wBsq % 8) % 2) == ((bBsq / 8 + bBsq % 8) % 2);
            return false;
        }
    }

    public class Matrix
    {
        public float[] Data;
        public int Rows, Cols;
        public Matrix(int rows, int cols) { Rows = rows; Cols = cols; Data = new float[rows * cols]; }
        public float this[int r, int c] { get => Data[r * Cols + c]; set => Data[r * Cols + c] = value; }
        public Matrix Clone() { var m = new Matrix(Rows, Cols); Array.Copy(Data, m.Data, Data.Length); return m; }

        public static Matrix operator *(Matrix a, Matrix b)
        {
            var r = new Matrix(a.Rows, b.Cols);
            for (int i = 0; i < a.Rows; i++)
                for (int k = 0; k < a.Cols; k++)
                {
                    float av = a[i, k];
                    for (int j = 0; j < b.Cols; j++)
                        r[i, j] += av * b[k, j];
                }
            return r;
        }
        public static Matrix operator +(Matrix a, Matrix b)
        {
            var r = new Matrix(a.Rows, a.Cols);
            if (a.Rows == b.Rows && a.Cols == b.Cols)
            {
                for (int i = 0; i < a.Data.Length; i++) r.Data[i] = a.Data[i] + b.Data[i];
            }
            else if (b.Rows == 1 && a.Cols == b.Cols)
            {
                for (int i = 0; i < a.Rows; i++)
                    for (int j = 0; j < a.Cols; j++)
                        r[i, j] = a[i, j] + b[0, j];
            }
            else
            {
                throw new ArgumentException($"Matrix dims mismatch for +: ({a.Rows}x{a.Cols}) vs ({b.Rows}x{b.Cols})");
            }
            return r;
        }
        public static Matrix operator -(Matrix a, Matrix b)
        {
            var r = new Matrix(a.Rows, a.Cols);
            if (a.Rows == b.Rows && a.Cols == b.Cols)
            {
                for (int i = 0; i < a.Data.Length; i++) r.Data[i] = a.Data[i] - b.Data[i];
            }
            else if (b.Rows == 1 && a.Cols == b.Cols)
            {
                for (int i = 0; i < a.Rows; i++)
                    for (int j = 0; j < a.Cols; j++)
                        r[i, j] = a[i, j] - b[0, j];
            }
            else
            {
                throw new ArgumentException($"Matrix dims mismatch for -: ({a.Rows}x{a.Cols}) vs ({b.Rows}x{b.Cols})");
            }
            return r;
        }
        public static Matrix operator *(Matrix a, float s) { var r = new Matrix(a.Rows, a.Cols); for (int i = 0; i < a.Data.Length; i++) r.Data[i] = a.Data[i] * s; return r; }

        public Matrix Transpose()
        {
            var r = new Matrix(Cols, Rows);
            for (int i = 0; i < Rows; i++)
                for (int j = 0; j < Cols; j++)
                    r[j, i] = this[i, j];
            return r;
        }
        public Matrix Map(Func<float, float> f) { var r = new Matrix(Rows, Cols); for (int i = 0; i < Data.Length; i++) r.Data[i] = f(Data[i]); return r; }

        public static Matrix SumRows(Matrix m)
        {
            var r = new Matrix(1, m.Cols);
            for (int i = 0; i < m.Rows; i++)
                for (int j = 0; j < m.Cols; j++)
                    r[0, j] += m[i, j];
            return r;
        }
    }

    public class ChessNet
    {
        public Matrix W1, b1, W2, b2, Wp, bp, Wv, bv;
        Random rng = new Random();

        const int IN = 768, H1 = 512, H2 = 256, POLICY = 20480;

        public ChessNet()
        {
            W1 = Xavier(IN, H1); b1 = new Matrix(1, H1);
            W2 = Xavier(H1, H2); b2 = new Matrix(1, H2);
            Wp = Xavier(H2, POLICY); bp = new Matrix(1, POLICY);
            Wv = Xavier(H2, 1); bv = new Matrix(1, 1);
        }

        Matrix Xavier(int fanIn, int fanOut)
        {
            float std = MathF.Sqrt(2.0f / fanIn);
            var m = new Matrix(fanIn, fanOut);
            for (int i = 0; i < m.Data.Length; i++) m.Data[i] = (float)(rng.NextDouble() * 2 - 1) * std;
            return m;
        }

        public Matrix[] Forward(Matrix input)
        {
            var z1 = input * W1 + b1;
            var h1 = z1.Map(x => x > 0 ? x : 0);
            var z2 = h1 * W2 + b2;
            var h2 = z2.Map(x => x > 0 ? x : 0);
            var zP = h2 * Wp + bp;
            var zV = h2 * Wv + bv;
            var v = zV.Map(MathF.Tanh);
            return new Matrix[] { zP, v, z1, h1, z2, h2 };
        }

        public float[] Backward(
            Matrix input, Matrix policyTarget, Matrix valueTarget, Matrix z1, Matrix h1, Matrix z2, Matrix h2, Matrix policyLogits, Matrix value,
            bool[] illegalMask, float illegalPenalty = 2.0f)
        {
            int batch = input.Rows;

            var dv = value - valueTarget;
            var dzV = dv.Clone();
            for (int i = 0; i < dzV.Data.Length; i++)
                dzV.Data[i] = dv.Data[i] * (1.0f - value.Data[i] * value.Data[i]);
            var dWv = h2.Transpose() * dzV * (1.0f / batch);
            var dbv = Matrix.SumRows(dzV) * (1.0f / batch);

            var probs = Softmax(policyLogits);
            var dzP = probs - policyTarget;
            if (illegalMask != null)
            {
                for (int i = 0; i < batch; i++)
                    if (illegalMask[i])
                        for (int j = 0; j < POLICY; j++)
                            dzP[i, j] += illegalPenalty * probs[i, j];
            }
            dzP = dzP * (1.0f / batch);
            var dWp = h2.Transpose() * dzP;
            var dbp = Matrix.SumRows(dzP);

            var dH2 = dzP * Wp.Transpose() + dzV * Wv.Transpose();
            var dz2 = dH2.Clone();
            for (int i = 0; i < dz2.Data.Length; i++)
                dz2.Data[i] = z2.Data[i] <= 0 ? 0 : dz2.Data[i];
            var dW2 = h1.Transpose() * dz2 * (1.0f / batch);
            var db2 = Matrix.SumRows(dz2) * (1.0f / batch);

            var dH1 = dz2 * W2.Transpose();
            var dz1 = dH1.Clone();
            for (int i = 0; i < dz1.Data.Length; i++)
                dz1.Data[i] = z1.Data[i] <= 0 ? 0 : dz1.Data[i];
            var dW1 = input.Transpose() * dz1 * (1.0f / batch);
            var db1 = Matrix.SumRows(dz1) * (1.0f / batch);

            float vLoss = 0, pLoss = 0;
            for (int i = 0; i < batch; i++)
            {
                float d = value.Data[i] - valueTarget.Data[i];
                vLoss += d * d;
                for (int j = 0; j < POLICY; j++)
                    if (policyTarget[i, j] > 0.5f)
                        pLoss -= MathF.Log(MathF.Max(probs[i, j], 1e-8f));
            }
            float loss = (vLoss / batch) + (pLoss / batch);

            return new float[] { loss };
        }

        public void Update(Matrix dW1, Matrix db1, Matrix dW2, Matrix db2, Matrix dWp, Matrix dbp, Matrix dWv, Matrix dbv)
        {
            float lr = 0.001f;
            ApplyGrad(dW1, W1, lr); ApplyGrad(db1, b1, lr);
            ApplyGrad(dW2, W2, lr); ApplyGrad(db2, b2, lr);
            ApplyGrad(dWp, Wp, lr); ApplyGrad(dbp, bp, lr);
            ApplyGrad(dWv, Wv, lr); ApplyGrad(dbv, bv, lr);
        }

        void ApplyGrad(Matrix grad, Matrix weight, float lr)
        {
            for (int i = 0; i < weight.Data.Length; i++)
                weight.Data[i] -= lr * grad.Data[i];
        }

        Matrix Softmax(Matrix logits)
        {
            var r = new Matrix(logits.Rows, logits.Cols);
            for (int i = 0; i < logits.Rows; i++)
            {
                float max = logits.Data[i * logits.Cols];
                for (int j = 1; j < logits.Cols; j++) if (logits.Data[i * logits.Cols + j] > max) max = logits.Data[i * logits.Cols + j];
                float sum = 0;
                for (int j = 0; j < logits.Cols; j++)
                {
                    float e = MathF.Exp(logits.Data[i * logits.Cols + j] - max);
                    r.Data[i * logits.Cols + j] = e;
                    sum += e;
                }
                for (int j = 0; j < logits.Cols; j++) r.Data[i * logits.Cols + j] /= sum;
            }
            return r;
        }
    }

    public class Experience
    {
        public float[] State;
        public int MoveIdx;
        public float Reward;
        public bool Illegal;
    }

    public class NeuralAgent
    {
        ChessNet net;
        public Color Color;
        public int IllegalAttempts = 0, LegalMoves = 0;
        public List<Experience> Buffer = new List<Experience>();
        Random rng = new Random();

        public NeuralAgent(ChessNet net, Color c) { this.net = net; Color = c; }

        public Move GetAction(Board board, out float value, out int moveIdx, out bool wasExploration, out bool wasIllegal)
        {
            var legal = MoveGenerator.GenerateLegalMoves(board);
            if (legal.Count == 0) { value = 0; moveIdx = -1; wasExploration = false; wasIllegal = false; return default; }

            var input = new Matrix(1, 768);
            Array.Copy(EncodeBoard(board), input.Data, 768);
            var fwd = net.Forward(input);
            var policyLogits = fwd[0];
            var val = fwd[1];
            value = val.Data[0];

            // MODO EXPLORACION ILEGAL (10%): elegir de TODO el espacio de policy
            // Esto fuerza a la red a aprender que movimientos son ilegales
            if (rng.NextDouble() < 0.10)
            {
                moveIdx = rng.Next(20480);
                wasExploration = true;
                // Verificar si por pura casualidad es legal
                foreach (var lm in legal)
                {
                    if (EncodeMove(lm) == moveIdx)
                    {
                        wasIllegal = false;
                        LegalMoves++;
                        return lm;
                    }
                }
                wasIllegal = true;
                IllegalAttempts++;
                return default; // PlayGame manejara el ilegal
            }

            // MODO EXPLORACION LEGAL (15%): elegir legal aleatorio
            if (rng.NextDouble() < 0.15)
            {
                int c = rng.Next(legal.Count);
                moveIdx = EncodeMove(legal[c]);
                wasExploration = true;
                wasIllegal = false;
                LegalMoves++;
                return legal[c];
            }

            // MODO RED NEURONAL: softmax sobre legales
            var legalIdx = new int[legal.Count];
            var legalScores = new float[legal.Count];
            for (int i = 0; i < legal.Count; i++)
            {
                legalIdx[i] = EncodeMove(legal[i]);
                legalScores[i] = policyLogits.Data[legalIdx[i]];
            }

            float max = legalScores[0];
            for (int i = 1; i < legalScores.Length; i++) if (legalScores[i] > max) max = legalScores[i];
            float sum = 0;
            for (int i = 0; i < legalScores.Length; i++) { legalScores[i] = MathF.Exp(legalScores[i] - max); sum += legalScores[i]; }
            for (int i = 0; i < legalScores.Length; i++) legalScores[i] /= sum;

            double r = rng.NextDouble();
            double cum = 0;
            int choice = legalScores.Length - 1;
            for (int i = 0; i < legalScores.Length; i++)
            {
                cum += legalScores[i];
                if (r <= cum) { choice = i; break; }
            }
            moveIdx = legalIdx[choice];
            wasExploration = false;
            wasIllegal = false;
            LegalMoves++;
            return legal[choice];
        }

        public void Record(float[] state, int idx, float reward, bool illegal)
        {
            Buffer.Add(new Experience { State = (float[])state.Clone(), MoveIdx = idx, Reward = reward, Illegal = illegal });
            if (illegal) IllegalAttempts++; else LegalMoves++;
        }

        public static float[] EncodeBoard(Board b)
        {
            float[] s = new float[768];
            for (int sq = 0; sq < 64; sq++)
            {
                var p = b.Squares[sq];
                if (p.Type != PieceType.None)
                {
                    int row = 7 - (sq / 8);
                    int col = sq % 8;
                    int ch = PieceToChannel(p);
                    s[ch * 64 + row * 8 + col] = 1.0f;
                }
            }
            return s;
        }

        static int PieceToChannel(Piece p)
        {
            int off = p.Color == Color.White ? 0 : 6;
            switch (p.Type)
            {
                case PieceType.Pawn: return off + 0;
                case PieceType.Knight: return off + 1;
                case PieceType.Bishop: return off + 2;
                case PieceType.Rook: return off + 3;
                case PieceType.Queen: return off + 4;
                case PieceType.King: return off + 5;
                default: return 0;
            }
        }

        public static int EncodeMove(Move m)
        {
            int pi = 0;
            switch (m.Promotion)
            {
                case PieceType.Queen: pi = 1; break;
                case PieceType.Rook: pi = 2; break;
                case PieceType.Bishop: pi = 3; break;
                case PieceType.Knight: pi = 4; break;
            }
            return m.From * 64 * 5 + m.To * 5 + pi;
        }
    }

    public class SelfPlaySystem
    {
        ChessNet model = new ChessNet();
        List<Experience> memory = new List<Experience>();
        Random rng = new Random();
        public Dictionary<string, object> Stats = new Dictionary<string, object>();
        const string MODEL_FILE = "chess_brain.bin";
        const string STATS_FILE = "chess_stats.json";
        const int MAX_GAME_TIME_MS = 8 * 60 * 1000; // 8 minutos

        int GetStatInt(string key)
        {
            if (!Stats.ContainsKey(key)) return 0;
            var val = Stats[key];
            if (val is int i) return i;
            if (val is long l) return (int)l;
            if (val is System.Text.Json.JsonElement je)
            {
                if (je.ValueKind == System.Text.Json.JsonValueKind.Number) return je.GetInt32();
            }
            if (val is float f) return (int)f;
            if (val is double d) return (int)d;
            if (val is string s && int.TryParse(s, out int parsed)) return parsed;
            return 0;
        }

        public SelfPlaySystem()
        {
            Stats["games_played"] = 0;
            Stats["wins_white"] = 0;
            Stats["wins_black"] = 0;
            Stats["draws"] = 0;
            Stats["total_illegal_attempts"] = 0;
            Stats["total_legal_moves"] = 0;
            Stats["timeouts"] = 0;
            LoadBrain();
            Console.WriteLine("Conciencia cargada. Partidas previas: " + GetStatInt("games_played"));
        }

        public void PlayGame(float delaySec, bool verbose)
        {
            var board = new Board();
            SetupInitial(board);
            var white = new NeuralAgent(model, Color.White);
            var black = new NeuralAgent(model, Color.Black);
            var moveHistory = new List<string>();
            var evalHistory = new List<float>();
            var stopwatch = Stopwatch.StartNew();
            bool timeOut = false;

            if (verbose)
            {
                Console.WriteLine("\nNUEVA PARTIDA INICIADA!");
                Console.WriteLine("BLANCAS vs NEGRAS");
                Console.WriteLine("Limite de tiempo: 5 minutos");
                Thread.Sleep((int)(delaySec * 1000));
            }

            while (true)
            {
                // Verificar tiempo limite
                if (stopwatch.ElapsedMilliseconds > MAX_GAME_TIME_MS)
                {
                    timeOut = true;
                    if (verbose) Console.WriteLine("\n[!] TIEMPO AGOTADO (>8 min). Partida terminada por limite de tiempo.");
                    break;
                }

                var legal = MoveGenerator.GenerateLegalMoves(board);
                var result = MoveGenerator.GameResult(board, legal);
                if (result != null) break;

                var agent = board.SideToMove == Color.White ? white : black;
                var state = NeuralAgent.EncodeBoard(board);

                var move = agent.GetAction(board, out float val, out int mIdx, out bool explored, out bool wasIllegal);
                evalHistory.Add(val);

                // Si el agente devolvio un movimiento ilegal intencional (exploracion ilegal)
                if (wasIllegal)
                {
                    if (verbose)
                    {
                        Console.ForegroundColor = ConsoleColor.Yellow;
                        Console.WriteLine("  [!] MOVIMIENTO ILEGAL DETECTADO: indice " + mIdx + " -> penalizacion aplicada");
                        Console.ResetColor();
                    }
                    agent.Record(state, mIdx, -5.0f, true);
                    move = legal[rng.Next(legal.Count)];
                    mIdx = NeuralAgent.EncodeMove(move);
                }
                else
                {
                    // Verificacion doble de seguridad
                    bool isLegal = false;
                    foreach (var lm in legal) if (NeuralAgent.EncodeMove(lm) == mIdx) { isLegal = true; break; }

                    if (!isLegal)
                    {
                        if (verbose)
                        {
                            Console.ForegroundColor = ConsoleColor.Red;
                            Console.WriteLine("  [X] ERROR CRITICO: Movimiento ilegal no detectado por agente: " + mIdx);
                            Console.ResetColor();
                        }
                        agent.Record(state, mIdx, -5.0f, true);
                        move = legal[rng.Next(legal.Count)];
                        mIdx = NeuralAgent.EncodeMove(move);
                    }
                    else
                    {
                        agent.Record(state, mIdx, 0.0f, false);
                    }
                }

                board.PushHistory();
                board.MakeMove(move);
                moveHistory.Add(move.UCI);

                if (verbose)
                {
                    DisplayBoard(board, val);
                    string side = board.SideToMove == Color.Black ? "W" : "B";
                    Console.WriteLine("  " + side + " Movimiento: " + move.UCI + (explored ? " [exploracion]" : " [red neuronal]"));
                    Thread.Sleep((int)(delaySec * 1000));
                }
            }

            stopwatch.Stop();
            string finalResult;
            string winner;
            float wReward, bReward;
            string terminationReason;

            if (timeOut)
            {
                finalResult = "1/2-1/2";
                wReward = -0.5f; // Castigo por lento
                bReward = -0.5f; // Castigo por lento
                winner = "EMPATE (TIEMPO)";
                terminationReason = "Tiempo agotado (>8 min)";
                Stats["timeouts"] = GetStatInt("timeouts") + 1;
                Stats["draws"] = GetStatInt("draws") + 1;
            }
            else
            {
                var finalLegal = MoveGenerator.GenerateLegalMoves(board);
                finalResult = MoveGenerator.GameResult(board, finalLegal);
                terminationReason = GetTerminationReason(board, finalLegal);

                if (finalResult == "1-0") { wReward = 1.0f; bReward = -1.0f; winner = "BLANCAS"; Stats["wins_white"] = GetStatInt("wins_white") + 1; }
                else if (finalResult == "0-1") { wReward = -1.0f; bReward = 1.0f; winner = "NEGRAS"; Stats["wins_black"] = GetStatInt("wins_black") + 1; }
                else { wReward = 0.2f; bReward = 0.2f; winner = "EMPATE"; Stats["draws"] = GetStatInt("draws") + 1; }
            }

            if (verbose)
            {
                Console.WriteLine("\nPARTIDA FINALIZADA");
                Console.WriteLine("Resultado: " + finalResult);
                Console.WriteLine("Motivo: " + terminationReason);
                Console.WriteLine("Movimientos: " + moveHistory.Count);
                Console.WriteLine("Duracion: " + stopwatch.Elapsed.ToString("mm\\:ss"));
                Console.WriteLine("Ganador: " + winner);
            }

            foreach (var e in white.Buffer) e.Reward = wReward;
            foreach (var e in black.Buffer) e.Reward = bReward;
            memory.AddRange(white.Buffer);
            memory.AddRange(black.Buffer);
            if (memory.Count > 50000) memory.RemoveRange(0, memory.Count - 50000);

            Stats["games_played"] = GetStatInt("games_played") + 1;
            Stats["total_illegal_attempts"] = GetStatInt("total_illegal_attempts") + white.IllegalAttempts + black.IllegalAttempts;
            Stats["total_legal_moves"] = GetStatInt("total_legal_moves") + white.LegalMoves + black.LegalMoves;

            if (memory.Count >= 32)
            {
                float loss = TrainStep();
                if (verbose) Console.WriteLine("\nEntrenamiento completado. Loss: " + loss.ToString("F4"));
            }

            if (verbose) PostGameAnalysis(moveHistory, evalHistory, white.IllegalAttempts + black.IllegalAttempts, white.LegalMoves + black.LegalMoves, winner, stopwatch.Elapsed);
            SaveBrain();
        }

        float TrainStep()
        {
            int batchSize = Math.Min(256, memory.Count);
            var batch = memory.OrderBy(x => rng.Next()).Take(batchSize).ToList();

            var inputs = new Matrix(batchSize, 768);
            var vTarget = new Matrix(batchSize, 1);
            var pTarget = new Matrix(batchSize, 20480);
            var illegalMask = new bool[batchSize];

            for (int i = 0; i < batchSize; i++)
            {
                Array.Copy(batch[i].State, 0, inputs.Data, i * 768, 768);
                vTarget[i, 0] = batch[i].Reward;
                pTarget[i, batch[i].MoveIdx] = 1.0f;
                illegalMask[i] = batch[i].Illegal;
            }

            var fwd = model.Forward(inputs);
            var policy = fwd[0];
            var value = fwd[1];
            var z1 = fwd[2];
            var h1 = fwd[3];
            var z2 = fwd[4];
            var h2 = fwd[5];

            var lossArr = model.Backward(inputs, pTarget, vTarget, z1, h1, z2, h2, policy, value, illegalMask, 2.0f);
            float loss = lossArr[0];

            var fwd2 = model.Forward(inputs);
            var grads = ComputeGradients(inputs, pTarget, vTarget, fwd2[2], fwd2[3], fwd2[4], fwd2[5], fwd2[0], fwd2[1], illegalMask, 2.0f);
            model.Update(grads.dW1, grads.db1, grads.dW2, grads.db2, grads.dWp, grads.dbp, grads.dWv, grads.dbv);

            return loss;
        }

        (Matrix dW1, Matrix db1, Matrix dW2, Matrix db2, Matrix dWp, Matrix dbp, Matrix dWv, Matrix dbv) ComputeGradients(
            Matrix input, Matrix policyTarget, Matrix valueTarget, Matrix z1, Matrix h1, Matrix z2, Matrix h2, Matrix policyLogits, Matrix value,
            bool[] illegalMask, float illegalPenalty)
        {
            int batch = input.Rows;

            var dv = value - valueTarget;
            var dzV = dv.Clone();
            for (int i = 0; i < dzV.Data.Length; i++)
                dzV.Data[i] = dv.Data[i] * (1.0f - value.Data[i] * value.Data[i]);
            var dWv = h2.Transpose() * dzV * (1.0f / batch);
            var dbv = Matrix.SumRows(dzV) * (1.0f / batch);

            var probs = Softmax(policyLogits);
            var dzP = probs - policyTarget;
            if (illegalMask != null)
            {
                for (int i = 0; i < batch; i++)
                    if (illegalMask[i])
                        for (int j = 0; j < 20480; j++)
                            dzP[i, j] += illegalPenalty * probs[i, j];
            }
            dzP = dzP * (1.0f / batch);
            var dWp = h2.Transpose() * dzP;
            var dbp = Matrix.SumRows(dzP);

            var dH2 = dzP * model.Wp.Transpose() + dzV * model.Wv.Transpose();
            var dz2 = dH2.Clone();
            for (int i = 0; i < dz2.Data.Length; i++)
                dz2.Data[i] = z2.Data[i] <= 0 ? 0 : dz2.Data[i];
            var dW2 = h1.Transpose() * dz2 * (1.0f / batch);
            var db2 = Matrix.SumRows(dz2) * (1.0f / batch);

            var dH1 = dz2 * model.W2.Transpose();
            var dz1 = dH1.Clone();
            for (int i = 0; i < dz1.Data.Length; i++)
                dz1.Data[i] = z1.Data[i] <= 0 ? 0 : dz1.Data[i];
            var dW1 = input.Transpose() * dz1 * (1.0f / batch);
            var db1 = Matrix.SumRows(dz1) * (1.0f / batch);

            return (dW1, db1, dW2, db2, dWp, dbp, dWv, dbv);
        }

        Matrix Softmax(Matrix logits)
        {
            var r = new Matrix(logits.Rows, logits.Cols);
            for (int i = 0; i < logits.Rows; i++)
            {
                float max = logits.Data[i * logits.Cols];
                for (int j = 1; j < logits.Cols; j++) if (logits.Data[i * logits.Cols + j] > max) max = logits.Data[i * logits.Cols + j];
                float sum = 0;
                for (int j = 0; j < logits.Cols; j++)
                {
                    float e = MathF.Exp(logits.Data[i * logits.Cols + j] - max);
                    r.Data[i * logits.Cols + j] = e;
                    sum += e;
                }
                for (int j = 0; j < logits.Cols; j++) r.Data[i * logits.Cols + j] /= sum;
            }
            return r;
        }

        void PostGameAnalysis(List<string> moves, List<float> evals, int illegal, int legal, string winner, TimeSpan duration)
        {
            Console.WriteLine("\n" + new string('=', 50));
            Console.WriteLine("     EVALUACION POST-PARTIDA");
            Console.WriteLine(new string('=', 50));

            int total = illegal + legal;
            float acc = total > 0 ? (legal / (float)total * 100f) : 0;
            Console.WriteLine("\nPrecision de legalidad: " + acc.ToString("F1") + "%");
            Console.WriteLine("   Jugadas legales: " + legal);
            Console.WriteLine("   Intentos ilegales: " + illegal);
            Console.WriteLine("   Duracion: " + duration.ToString("mm\\:ss"));
            Console.WriteLine("   Ratio aprendizaje: " + acc.ToString("F1") + "%");

            if (evals.Count > 0)
            {
                float avg = 0;
                foreach (var v in evals) avg += v;
                avg /= evals.Count;
                double var = 0;
                foreach (var v in evals) var += (v - avg) * (v - avg);
                var /= evals.Count;
                int blunders = 0;
                for (int i = 1; i < evals.Count; i++) if (Math.Abs(evals[i] - evals[i - 1]) > 0.8f) blunders++;
                Console.WriteLine("\nEvaluacion promedio: " + avg.ToString("+0.000;-0.000;0.000"));
                Console.WriteLine("Varianza: " + var.ToString("F3"));
                Console.WriteLine("Errores graves: " + blunders);
            }

            int games = GetStatInt("games_played");
            Console.WriteLine("\nGlobal: Partidas=" + games + ", W=" + Stats["wins_white"] + ", B=" + Stats["wins_black"] + ", D=" + Stats["draws"] + ", T=" + Stats["timeouts"]);
            float skill = Math.Min(100, acc * 0.7f + (games / 10f));
            Console.WriteLine("\nNivel de conciencia: " + skill.ToString("F1") + "/100");
            if (skill < 20) Console.WriteLine("   Estado: Neonata");
            else if (skill < 50) Console.WriteLine("   Estado: Infantil");
            else if (skill < 75) Console.WriteLine("   Estado: Estudiante");
            else Console.WriteLine("   Estado: Maestra");
            Console.WriteLine(new string('=', 50));
        }

        string GetTerminationReason(Board b, List<Move> legal)
        {
            if (legal.Count == 0) return MoveGenerator.IsInCheck(b, b.SideToMove) ? "Jaque mate" : "Ahogado";
            if (b.HalfmoveClock >= 100) return "Regla 50 movimientos";
            if (MoveGenerator.InsufficientMaterial(b)) return "Material insuficiente";
            var current = b.ToFenKey();
            int reps = 0;
            foreach (var h in b.PositionHistory) if (h == current) reps++;
            if (reps >= 2) return "Repeticion triple";
            return "Desconocido";
        }

        void DisplayBoard(Board b, float eval)
        {
            Console.WriteLine("\n" + new string('=', 35));
            Console.WriteLine("  Turno: " + (b.SideToMove == Color.White ? "BLANCAS" : "NEGRAS"));
            Console.WriteLine(new string('-', 35));
            Console.WriteLine("    a b c d e f g h");
            for (int r = 7; r >= 0; r--)
            {
                Console.Write("  " + (r + 1) + " ");
                for (int c = 0; c < 8; c++)
                {
                    var p = b.Squares[r * 8 + c];
                    char sym = p.Type == PieceType.None ? '.' : p.ToChar();
                    if (p.Type != PieceType.None)
                    {
                        if (p.Color == Color.White)
                            Console.ForegroundColor = ConsoleColor.Blue;
                        else
                            Console.ForegroundColor = ConsoleColor.Red;
                    }
                    Console.Write(sym + " ");
                    Console.ResetColor();
                }
                Console.WriteLine();
            }
            Console.WriteLine(new string('-', 35));
            int barLen = 20, pos = (int)((eval + 1) / 2 * barLen);
            pos = Math.Max(0, Math.Min(barLen - 1, pos));
            char[] bar = new char[barLen];
            for (int i = 0; i < barLen; i++) bar[i] = '-';
            bar[pos] = '*';
            Console.WriteLine("  Eval: [" + new string(bar) + "] " + eval.ToString("+0.00;-0.00;0.00"));
            Console.WriteLine(new string('=', 35) + "\n");
        }

        void SetupInitial(Board b)
        {
            PieceType[] backRank = { PieceType.Rook, PieceType.Knight, PieceType.Bishop, PieceType.Queen, PieceType.King, PieceType.Bishop, PieceType.Knight, PieceType.Rook };
            for (int i = 0; i < 8; i++)
            {
                b.Squares[0 + i] = new Piece { Type = backRank[i], Color = Color.White };
                b.Squares[8 + i] = new Piece { Type = PieceType.Pawn, Color = Color.White };
                b.Squares[48 + i] = new Piece { Type = PieceType.Pawn, Color = Color.Black };
                b.Squares[56 + i] = new Piece { Type = backRank[i], Color = Color.Black };
            }
        }

        void SaveBrain()
        {
            using var fs = new FileStream(MODEL_FILE, FileMode.Create);
            using var bw = new BinaryWriter(fs);
            foreach (var m in new Matrix[] { model.W1, model.b1, model.W2, model.b2, model.Wp, model.bp, model.Wv, model.bv })
            {
                bw.Write(m.Rows); bw.Write(m.Cols);
                foreach (var f in m.Data) bw.Write(f);
            }
            File.WriteAllText(STATS_FILE, JsonSerializer.Serialize(Stats, new JsonSerializerOptions { WriteIndented = true }));
            Console.WriteLine("Conciencia guardada.");
        }

        void LoadBrain()
        {
            if (!File.Exists(MODEL_FILE)) return;
            try
            {
                using var fs = new FileStream(MODEL_FILE, FileMode.Open);
                using var br = new BinaryReader(fs);
                foreach (var m in new Matrix[] { model.W1, model.b1, model.W2, model.b2, model.Wp, model.bp, model.Wv, model.bv })
                {
                    int r = br.ReadInt32(), c = br.ReadInt32();
                    for (int i = 0; i < m.Data.Length; i++) m.Data[i] = br.ReadSingle();
                }
                Console.WriteLine("Pesos neuronales restaurados desde disco.");
            }
            catch { Console.WriteLine("Error cargando pesos, iniciando cerebro virgen."); }

            if (File.Exists(STATS_FILE))
            {
                try
                {
                    var json = File.ReadAllText(STATS_FILE);
                    Stats = JsonSerializer.Deserialize<Dictionary<string, object>>(json);
                }
                catch { }
            }
        }
    }

    class Program
    {
        static void Main(string[] args)
        {
            Console.OutputEncoding = System.Text.Encoding.UTF8;
            PrintBanner();
            var system = new SelfPlaySystem();

            while (true)
            {
                Console.WriteLine("\n" + new string('-', 40));
                Console.WriteLine("  MENU PRINCIPAL");
                Console.WriteLine(new string('-', 40));
                Console.WriteLine("  [1] Iniciar partida (IA vs IA)");
                Console.WriteLine("  [2] Reiniciar conciencia");
                Console.WriteLine("  [3] Ver estadisticas");
                Console.WriteLine("  [4] Salir");
                Console.WriteLine(new string('-', 40));
                Console.Write("  Selecciona opcion: ");
                var choice = Console.ReadLine()?.Trim();

                if (choice == "1")
                {
                    Console.Write("\nSegundos entre movimientos (default 1.0): ");
                    if (!float.TryParse(Console.ReadLine(), out float delay)) delay = 1.0f;
                    system.PlayGame(delay, true);
                    Console.Write("\nOtra partida? (1=Si / 2=No): ");
                    if (Console.ReadLine()?.Trim() != "1") break;
                }
                else if (choice == "2")
                {
                    Console.Write("Escribe BORRAR para confirmar: ");
                    if (Console.ReadLine()?.Trim() == "BORRAR")
                    {
                        if (File.Exists("chess_brain.bin")) File.Delete("chess_brain.bin");
                        if (File.Exists("chess_stats.json")) File.Delete("chess_stats.json");
                        system = new SelfPlaySystem();
                        Console.WriteLine("Memoria borrada. IA reiniciada.");
                    }
                    else Console.WriteLine("Cancelado.");
                }
                else if (choice == "3")
                {
                    Console.WriteLine("\n" + new string('=', 40));
                    Console.WriteLine("  ESTADISTICAS GLOBALES");
                    Console.WriteLine(new string('=', 40));
                    foreach (var kv in system.Stats) Console.WriteLine("  " + kv.Key + ": " + kv.Value);
                    Console.WriteLine(new string('=', 40));
                }
                else if (choice == "4") { Console.WriteLine("Hasta luego."); break; }
                else Console.WriteLine("Opcion no valida.");
            }
        }

        static void PrintBanner()
        {
            Console.WriteLine(@"
    ==========================================
         NEURAL CHESS SELF-PLAY
    ==========================================
    IA Autonoma en C# .NET
    Motor + Red Neuronal puramente desde cero.
    Sin dependencias.

    Aprende de ilegales, guarda pesos, recuerda.
    ==========================================
            ");
        }
    }
}