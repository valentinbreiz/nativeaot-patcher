# Generations

Generational collectors split the heap by object age. The generational hypothesis observes that most objects die young, so collecting only the youngest region (generation 0) reclaims most of the garbage at a fraction of the cost of a full-heap scan; older generations are collected rarely. The highest generation a given collection covers is called the condemned generation.

That optimization needs machinery. The collector must find references from old objects into the young generation without scanning the old one, so every reference store passes through a write barrier, a small piece of code that records old-to-young writes in a side structure (a remembered set or card table) the collector consults during young collections.

A single-generation collector scans the whole heap on every collection. Each collection costs more, but there are no write barriers and no remembered sets, and reference stores are plain memory writes.

OrionGC has a single generation and reports every heap object as generation 0. See [Statistics and memory info](../garbage-collector.md#statistics-and-memory-info).
