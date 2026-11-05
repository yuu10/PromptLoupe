using System;
using System.Collections.Generic;
using System.Globalization;

namespace PromptLoupe;

/// <summary>
/// エクスプローラー風の自然順(「img2」が「img10」より前)で文字列を比べる。
/// Windows の StrCmpLogicalW の代わり(Mac・Linux でも使えるように自前で実装)。
/// - 数字の並びは数として比べる(先頭の 0 は無視。同じ数なら 0 の少ない方が前)
/// - それ以外は大文字・小文字を区別せずに比べる
/// - 数字は文字より前(「a1」は「ab」より前)
/// </summary>
public sealed class NaturalComparer : IComparer<string>
{
    public static readonly NaturalComparer Instance = new();

    static readonly CompareInfo Ci = CultureInfo.InvariantCulture.CompareInfo;

    public int Compare(string? a, string? b)
    {
        if (ReferenceEquals(a, b)) return 0;
        if (a == null) return -1;
        if (b == null) return 1;

        int i = 0, j = 0;
        int zeroTie = 0; // 数は同じで、先頭の 0 の数だけが違うとき(最後の決め手にする)

        while (i < a.Length && j < b.Length)
        {
            bool da = char.IsAsciiDigit(a[i]);
            bool db = char.IsAsciiDigit(b[j]);

            if (da && db)
            {
                int si = i, sj = j;
                while (i < a.Length && char.IsAsciiDigit(a[i])) i++;
                while (j < b.Length && char.IsAsciiDigit(b[j])) j++;

                // 先頭の 0 を除いて、桁数 → 各桁の順に比べる(long に収まらない長さでも比べられる)
                int zi = si, zj = sj;
                while (zi < i - 1 && a[zi] == '0') zi++;
                while (zj < j - 1 && b[zj] == '0') zj++;
                int lenA = i - zi, lenB = j - zj;
                if (lenA != lenB) return lenA < lenB ? -1 : 1;
                for (int k = 0; k < lenA; k++)
                    if (a[zi + k] != b[zj + k]) return a[zi + k] < b[zj + k] ? -1 : 1;

                if (zeroTie == 0 && (zi - si) != (zj - sj)) zeroTie = (zi - si) < (zj - sj) ? -1 : 1;
                continue;
            }

            if (da != db) return da ? -1 : 1; // 数字は文字より前

            // 数字以外の並びを、まとめて比べる
            int ei = i, ej = j;
            while (ei < a.Length && !char.IsAsciiDigit(a[ei])) ei++;
            while (ej < b.Length && !char.IsAsciiDigit(b[ej])) ej++;
            int r = Ci.Compare(a, i, ei - i, b, j, ej - j, CompareOptions.IgnoreCase);
            if (r != 0) return r < 0 ? -1 : 1;
            i = ei;
            j = ej;
        }

        if (i < a.Length) return 1;  // b が a の先頭部分
        if (j < b.Length) return -1;
        if (zeroTie != 0) return zeroTie;
        return string.CompareOrdinal(a, b);
    }
}
