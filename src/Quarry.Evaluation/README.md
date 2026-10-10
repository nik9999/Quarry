# Quarry.Evaluation

Evaluation for the **Quarry** RAG stack — offline and online quality measurement:
retrieval metrics, groundedness/faithfulness judges, and ground-truth scoring used to track
answer quality and catch regressions before they ship.

## Features

- **RetrievalEvaluator**: Computes standard IR/RAG metrics over retrieval results:
  - Recall@K
  - Precision@K
  - Mean Reciprocal Rank (MRR)
  - Normalized Discounted Cumulative Gain (nDCG@K)
- **GenerationEvaluator**: LLM-as-judge faithfulness / groundedness scorer grading answer claims against retrieved contexts.
- **StatisticalSignificance**: Pure statistical toolkit for evaluating whether metric deltas on evaluation sets are statistically significant:
  - `WilsonInterval`: Asymmetric binomial proportion confidence interval well-behaved near 0/1 and small samples.
  - `PairedBootstrapDelta`: Seeded paired bootstrap confidence interval for mean delta between pipeline versions on the same seed set.
  - `BootstrapMean`: Bootstrap confidence interval for a single pipeline's metric distribution.
  - `McNemar`: Paired chi-square test with continuity correction for discordant pass/fail flips.

## Dependencies

- `Quarry.Core`
- `Microsoft.Extensions.AI.Abstractions`
- `Microsoft.Extensions.Logging.Abstractions`
