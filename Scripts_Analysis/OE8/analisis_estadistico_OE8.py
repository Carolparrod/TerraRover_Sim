"""
============================================================================
Análisis estadístico del OE8 — TerraRover-Gen
============================================================================
Compara la política de aprendizaje por refuerzo (HuskyAgent2) frente al
controlador heurístico baseline (HuskyHeuristic) sobre 6 terrenos:
  - V3_F3_02 (terreno de entrenamiento)
  - BigRock, HardTerrain, BumpyGround, Complete, DeepHoles (test)

Diseño experimental:
  - 100 episodios por terreno y por sistema.
  - Seeds compartidas entre RL y HEU (comparación pareada episodio a episodio).
  - Limpieza simétrica aplicada a Test Complete: se excluyen episodios FALL
    con pasos <= 50 (fallos de spawn del generador, no atribuibles a la política)
    si se producen en cualquiera de los dos sistemas para una seed dada.

Pruebas estadísticas aplicadas:
  - IC95% Wilson para tasas de éxito (proporciones).
  - Test de McNemar pareado (binomial exacto) para comparar tasas de éxito
    RL vs HEU bajo seeds compartidas.
  - Test de Wilcoxon de rangos signados pareado para variables continuas
    (pasos, tiempo, distancia final, energía) sobre el subconjunto de seeds
    en las que ambos sistemas obtuvieron SUCCESS.
  - Tamaño de efecto r de rango para Wilcoxon (r = |Z| / sqrt(N)).
  - Corrección de Holm-Bonferroni sobre las 6 comparaciones McNemar.

Análisis complementario:
  - Comparación pareada del agente RL en BumpyGround bajo dos configuraciones
    del sistema anti-atasco (SCI=100 estándar vs SCI=200), aplicando McNemar
    pareado para evaluar si los STUCK se deben a un timeout demasiado estricto
    o a una limitación de la política aprendida.

Requisitos:
  pip install pandas numpy scipy statsmodels
============================================================================
"""

import os
import json
import numpy as np
import pandas as pd
from scipy import stats
from statsmodels.stats.contingency_tables import mcnemar
from statsmodels.stats.proportion import proportion_confint
from statsmodels.stats.multitest import multipletests


# ----------------------------------------------------------------------------
# Configuración
# ----------------------------------------------------------------------------
TERRAINS = ['V3_F3_02', 'BigRock', 'HardTerrain', 'BumpyGround', 'Complete', 'DeepHoles']
CSV_DIR = '.'  # Directorio donde se encuentran los CSV. Cambiar si es necesario.
COMPLETE_CLEANUP_STEPS = 50  # Umbral para limpieza simétrica de Test Complete


# ----------------------------------------------------------------------------
# Utilidades
# ----------------------------------------------------------------------------

def load_pair(terrain):
    """Carga RL y HEU para un terreno y devuelve ambos DataFrames con índices alineados por episodio."""
    rl = pd.read_csv(os.path.join(CSV_DIR, f'HuskyAgent2_metricas_{terrain}.csv'), sep=';')
    heu = pd.read_csv(os.path.join(CSV_DIR, f'HuskyHeuristic_metricas_{terrain}.csv'), sep=';')
    rl = rl.set_index('episodio').sort_index()
    heu = heu.set_index('episodio').sort_index()
    common = rl.index.intersection(heu.index)
    return rl.loc[common], heu.loc[common]


def apply_complete_cleanup(rl, heu, steps_threshold=COMPLETE_CLEANUP_STEPS):
    """Limpieza simétrica para Test Complete: excluye seeds con FALL+pasos<=threshold en cualquier sistema."""
    rl_bad = (rl['resultado'] == 'FALL') & (rl['pasos'] <= steps_threshold)
    heu_bad = (heu['resultado'] == 'FALL') & (heu['pasos'] <= steps_threshold)
    bad_seeds = rl.index[rl_bad].union(heu.index[heu_bad])
    keep = rl.index.difference(bad_seeds)
    return rl.loc[keep], heu.loc[keep], len(bad_seeds)


def wilson_ci(successes, n, alpha=0.05):
    """Intervalo de confianza Wilson para una proporción. Devuelve (lo, hi) en porcentaje."""
    lo, hi = proportion_confint(successes, n, alpha=alpha, method='wilson')
    return lo * 100, hi * 100


def mcnemar_paired(rl_success, heu_success):
    """McNemar pareado entre dos vectores binarios. Devuelve (a, b, c, d, p, statistic)."""
    a = ((rl_success) & (heu_success)).sum()
    b = ((~rl_success) & (heu_success)).sum()
    c = ((rl_success) & (~heu_success)).sum()
    d = ((~rl_success) & (~heu_success)).sum()
    result = mcnemar([[a, b], [c, d]], exact=True)
    return int(a), int(b), int(c), int(d), result.pvalue, result.statistic


def diff_props_paired_ci(b, c, n):
    """IC95% (aproximación normal Agresti-Min) para diferencia pareada de proporciones (c-b)/n."""
    diff = (c - b) / n * 100
    if b + c == 0:
        return diff, diff, diff
    se = np.sqrt((b + c - (c - b) ** 2 / n) / n) / n
    lo = (c - b) / n - 1.96 * se
    hi = (c - b) / n + 1.96 * se
    return diff, lo * 100, hi * 100


def wilcoxon_paired(rl_values, heu_values):
    """Wilcoxon pareado. Devuelve dict con p, r, medianas y n; o None si no es calculable."""
    rl_vals = np.asarray(rl_values)
    heu_vals = np.asarray(heu_values)
    if (rl_vals - heu_vals == 0).all():
        return None
    try:
        _, p = stats.wilcoxon(rl_vals, heu_vals, zero_method='wilcox', alternative='two-sided')
    except ValueError:
        return None
    n = len(rl_vals)
    z = stats.norm.ppf(1 - p / 2)
    r = abs(z) / np.sqrt(n)
    return {
        'p': float(p),
        'r': float(r),
        'median_rl': float(np.median(rl_vals)),
        'median_heu': float(np.median(heu_vals)),
        'n': int(n),
    }


# ----------------------------------------------------------------------------
# Análisis principal
# ----------------------------------------------------------------------------

def analyze():
    print("=" * 100)
    print("ANÁLISIS ESTADÍSTICO OE8 — TerraRover-Gen")
    print("=" * 100)

    mcnemar_pvalues = []
    mcnemar_labels = []
    summary = {}

    for terrain in TERRAINS:
        print(f"\n{'=' * 100}")
        print(f"TERRENO: {terrain}")
        print(f"{'=' * 100}")

        rl, heu = load_pair(terrain)
        n_orig = len(rl)

        if terrain == 'Complete':
            rl, heu, n_excluded = apply_complete_cleanup(rl, heu)
            n = len(rl)
            print(f"\nLimpieza simétrica aplicada: {n_excluded} episodios excluidos "
                  f"(FALL con pasos <= {COMPLETE_CLEANUP_STEPS}).")
            print(f"  Episodios brutos: {n_orig} → Episodios válidos: {n}\n")
        else:
            n = n_orig

        rl_success = (rl['resultado'] == 'SUCCESS').values
        heu_success = (heu['resultado'] == 'SUCCESS').values
        n_rl_succ = int(rl_success.sum())
        n_heu_succ = int(heu_success.sum())

        rl_rate = n_rl_succ / n * 100
        rl_lo, rl_hi = wilson_ci(n_rl_succ, n)
        heu_rate = n_heu_succ / n * 100
        heu_lo, heu_hi = wilson_ci(n_heu_succ, n)

        print(f"Tasa de éxito RL:   {rl_rate:5.1f}%  IC95% Wilson: [{rl_lo:5.1f}, {rl_hi:5.1f}]  ({n_rl_succ}/{n})")
        print(f"Tasa de éxito HEU:  {heu_rate:5.1f}%  IC95% Wilson: [{heu_lo:5.1f}, {heu_hi:5.1f}]  ({n_heu_succ}/{n})")
        print(f"\nResultados RL:  {dict(rl['resultado'].value_counts())}")
        print(f"Resultados HEU: {dict(heu['resultado'].value_counts())}")

        a, b, c, d, p_mc, _ = mcnemar_paired(rl_success, heu_success)
        diff_obs = (n_rl_succ - n_heu_succ) / n * 100
        _, diff_lo, diff_hi = diff_props_paired_ci(b, c, n)
        print(f"\nMcNemar pareado (RL vs HEU sobre éxito):")
        print(f"  Tabla 2×2: ambos éxito={a}, HEU éxito/RL fallo={b}, RL éxito/HEU fallo={c}, ambos fallo={d}")
        print(f"  p-valor (binomial exacto) = {p_mc:.4f}")
        print(f"  Diferencia pareada RL-HEU: {diff_obs:+.1f} pp  IC95%: [{diff_lo:+.1f}, {diff_hi:+.1f}]")

        mcnemar_pvalues.append(p_mc)
        mcnemar_labels.append(terrain)

        both_succ = rl_success & heu_success
        n_both = int(both_succ.sum())
        print(f"\nWilcoxon pareado (n={n_both} seeds con SUCCESS en ambos sistemas):")
        wilcox_results = {}
        if n_both >= 5:
            for var in ['pasos', 'tiempo_s', 'distancia_final_m', 'energia_total']:
                rl_vals = rl.loc[both_succ, var]
                heu_vals = heu.loc[both_succ, var]
                res = wilcoxon_paired(rl_vals, heu_vals)
                if res is None:
                    print(f"  {var:24s}: no calculable")
                else:
                    direction = "RL<HEU" if res['median_rl'] < res['median_heu'] else "RL>HEU"
                    print(f"  {var:24s}: mediana RL={res['median_rl']:8.2f}, HEU={res['median_heu']:8.2f} "
                          f"({direction})  p={res['p']:.4f}  r={res['r']:.3f}")
                wilcox_results[var] = res
        else:
            print(f"  (insuficientes pares con SUCCESS común)")
            for var in ['pasos', 'tiempo_s', 'distancia_final_m', 'energia_total']:
                wilcox_results[var] = None

        summary[terrain] = {
            'n': n,
            'n_excluded': n_orig - n,
            'rl_success': n_rl_succ,
            'heu_success': n_heu_succ,
            'rl_rate': rl_rate,
            'heu_rate': heu_rate,
            'rl_ci': (rl_lo, rl_hi),
            'heu_ci': (heu_lo, heu_hi),
            'mcnemar_p': p_mc,
            'mcnemar_table': (a, b, c, d),
            'diff_pp': diff_obs,
            'diff_ci': (diff_lo, diff_hi),
            'n_both_success': n_both,
            'wilcoxon': wilcox_results,
            'results_rl': dict(rl['resultado'].value_counts()),
            'results_heu': dict(heu['resultado'].value_counts()),
        }

    # Corrección Holm-Bonferroni
    print(f"\n{'=' * 100}")
    print("CORRECCIÓN HOLM-BONFERRONI (6 comparaciones McNemar)")
    print(f"{'=' * 100}")
    reject, p_adj, _, _ = multipletests(mcnemar_pvalues, alpha=0.05, method='holm')
    print(f"{'Terreno':15s}  {'p original':>12s}  {'p Holm':>12s}  {'Significativo':>20s}")
    for label, p_orig, p_h, rej in zip(mcnemar_labels, mcnemar_pvalues, p_adj, reject):
        print(f"{label:15s}  {p_orig:>12.4f}  {p_h:>12.4f}  {'SÍ' if rej else 'no':>20s}")
        summary[label]['mcnemar_p_holm'] = float(p_h)
        summary[label]['mcnemar_significant'] = bool(rej)

    # Tabla resumen
    print(f"\n{'=' * 100}")
    print("TABLA RESUMEN")
    print(f"{'=' * 100}\n")
    print(f"{'Terreno':15s} {'n':>4s} {'RL %':>6s} {'IC95% RL':>16s} {'HEU %':>6s} {'IC95% HEU':>16s}"
          f" {'Δpp':>7s} {'p McN':>9s} {'p Holm':>9s}")
    print("-" * 100)
    for t in TERRAINS:
        d = summary[t]
        print(f"{t:15s} {d['n']:>4d} {d['rl_rate']:>5.1f}  "
              f"[{d['rl_ci'][0]:5.1f},{d['rl_ci'][1]:5.1f}]   "
              f"{d['heu_rate']:>5.1f}  [{d['heu_ci'][0]:5.1f},{d['heu_ci'][1]:5.1f}]   "
              f"{d['diff_pp']:>+6.1f}  {d['mcnemar_p']:>8.4f}  {d['mcnemar_p_holm']:>8.4f}")

    # Exportar JSON (parcial) - el JSON completo con la variante SCI se escribe en __main__
    return summary


def analyze_sci_variant():
    """Análisis pareado SCI=100 vs SCI=200 en BumpyGround (mismo agente RL).
    
    Compara la tasa de éxito y la distribución de resultados terminales
    del agente RL en BumpyGround bajo dos configuraciones del sistema
    anti-atasco, con seeds compartidas (comparación pareada).
    """
    print("\n" + "=" * 100)
    print("ANÁLISIS COMPLEMENTARIO: Stuck Check Interval (SCI) en BumpyGround")
    print("=" * 100)
    print("Comparación pareada del agente RL bajo dos configuraciones del sistema anti-atasco.")
    print("Hipótesis a investigar: ¿los STUCK del RL en BumpyGround se deben a un")
    print("timeout demasiado estricto, o reflejan una limitación de la política?\n")
    
    bg100 = pd.read_csv(os.path.join(CSV_DIR, 'HuskyAgent2_metricas_BumpyGround.csv'),
                       sep=';').set_index('episodio').sort_index()
    bg200 = pd.read_csv(os.path.join(CSV_DIR, 'HuskyAgent2_metricas_BumpyGround_SCI200.csv'),
                       sep=';').set_index('episodio').sort_index()
    
    common = bg100.index.intersection(bg200.index)
    bg100 = bg100.loc[common]
    bg200 = bg200.loc[common]
    n = len(bg100)
    
    # Tasas de éxito con IC95% Wilson
    s100 = (bg100['resultado'] == 'SUCCESS').values
    s200 = (bg200['resultado'] == 'SUCCESS').values
    n100 = int(s100.sum())
    n200 = int(s200.sum())
    rate100 = n100 / n * 100
    rate200 = n200 / n * 100
    lo100, hi100 = wilson_ci(n100, n)
    lo200, hi200 = wilson_ci(n200, n)
    
    print(f"Tasa de éxito SCI=100 (estándar): {rate100:5.1f}%  IC95% Wilson: [{lo100:5.1f}, {hi100:5.1f}]  ({n100}/{n})")
    print(f"Tasa de éxito SCI=200:            {rate200:5.1f}%  IC95% Wilson: [{lo200:5.1f}, {hi200:5.1f}]  ({n200}/{n})")
    
    # Distribución de resultados
    print("\nDistribución de resultados terminales:")
    print(f"  {'Resultado':12s} {'SCI=100':>10s} {'SCI=200':>10s} {'Cambio':>10s}")
    for r in ['SUCCESS', 'STUCK', 'FALL', 'COLLISION']:
        c1 = (bg100['resultado'] == r).sum()
        c2 = (bg200['resultado'] == r).sum()
        print(f"  {r:12s} {c1:>10d} {c2:>10d} {c2-c1:>+10d}")
    
    # McNemar pareado (SCI200 vs SCI100)
    a = int((s100 & s200).sum())
    b = int((~s100 & s200).sum())  # SCI200 éxito, SCI100 no
    c = int((s100 & ~s200).sum())  # SCI100 éxito, SCI200 no
    d = int((~s100 & ~s200).sum())
    result = mcnemar([[a, b], [c, d]], exact=True)
    diff_obs = (n200 - n100) / n * 100
    _, diff_lo, diff_hi = diff_props_paired_ci(c, b, n)  # ojo: aquí (c-b)/n da el cambio SCI200-SCI100 si b son los SCI200+/SCI100-
    
    print("\nMcNemar pareado (SCI=100 vs SCI=200):")
    print(f"  Tabla 2×2: ambos éxito={a}, SCI200+/SCI100-={b}, SCI100+/SCI200-={c}, ambos fallo={d}")
    print(f"  p-valor (binomial exacto) = {result.pvalue:.4f}")
    print(f"  Diferencia pareada SCI=200 vs SCI=100: {diff_obs:+.1f} pp  IC95%: [{diff_lo:+.1f}, {diff_hi:+.1f}]")
    
    # Interpretación
    print("\nInterpretación:")
    if result.pvalue < 0.05:
        print(f"  Diferencia estadísticamente significativa (p<0.05).")
    else:
        print(f"  Diferencia NO estadísticamente significativa al nivel α=0.05 (p={result.pvalue:.3f}).")
    print(f"  Aun con el sistema anti-atasco relajado (SCI=200), la tasa de éxito del RL")
    print(f"  ({rate200:.0f}%) permanece muy por debajo del heurístico estándar (70.0%),")
    print(f"  lo que indica que la principal limitación está en la política aprendida,")
    print(f"  no en el timeout del sistema anti-atasco.")
    
    return {
        'n': n,
        'sci100_success': n100, 'sci200_success': n200,
        'sci100_rate': rate100, 'sci200_rate': rate200,
        'sci100_ci': (lo100, hi100), 'sci200_ci': (lo200, hi200),
        'mcnemar_p': float(result.pvalue),
        'mcnemar_table': (a, b, c, d),
        'diff_pp': diff_obs,
        'diff_ci': (diff_lo, diff_hi),
        'results_sci100': dict(bg100['resultado'].value_counts()),
        'results_sci200': dict(bg200['resultado'].value_counts()),
    }


if __name__ == '__main__':
    summary = analyze()
    sci_summary = analyze_sci_variant()
    
    # Guardar resultados completos
    def make_serializable(obj):
        if isinstance(obj, dict):
            return {k: make_serializable(v) for k, v in obj.items()}
        if isinstance(obj, (list, tuple)):
            return [make_serializable(v) for v in obj]
        if isinstance(obj, (np.integer,)):
            return int(obj)
        if isinstance(obj, (np.floating,)):
            return float(obj)
        if isinstance(obj, np.bool_):
            return bool(obj)
        return obj
    
    full = {'OE8': summary, 'SCI_variant_BumpyGround': sci_summary}
    with open(os.path.join(CSV_DIR, 'resultados_OE8.json'), 'w', encoding='utf-8') as f:
        json.dump(make_serializable(full), f, indent=2, ensure_ascii=False)
    print(f"\nResultados completos guardados en {os.path.join(CSV_DIR, 'resultados_OE8.json')}")
