/// Walks every page of a list operation, yielding each item. Generated
/// `...All` methods use it: [fetch] loads the page after a cursor (null for
/// the first page), and [read] returns the page's items and next cursor.
Stream<T> paginate<P, T>(
  Future<P> Function(String? cursor) fetch,
  (List<T>, String?) Function(P page) read,
) async* {
  String? cursor;
  do {
    final (items, next) = read(await fetch(cursor));
    yield* Stream.fromIterable(items);
    cursor = next;
  } while (cursor != null);
}
