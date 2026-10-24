// Spec 0004 CLN-4, spec 0014 PAG-2, PAG-6: the page type and the lazy walk
// over every page, tested once here where the templates live (PKG-3). Hand
// written; the generator never touches test/core.
import 'dart:async';

import 'package:orvano_server/src/core/exceptions.dart';
import 'package:orvano_server/src/core/paging.dart';
import 'package:test/test.dart';

void main() {
  group('ResultPage', () {
    test('reads items, nextCursor, and total, and writes them back', () {
      final json = <String, Object?>{
        'items': ['a', 'b'],
        'nextCursor': 'c2',
        'total': null,
      };
      final page = ResultPage.fromJson(json, (value, path) => value! as String);
      expect(page.items, ['a', 'b']);
      expect(page.nextCursor, 'c2');
      expect(page.total, isNull);
      expect(page.toJson((item) => item), json);
    });

    test('names the path of a bad field', () {
      expect(
        () => ResultPage.fromJson({
          'items': [],
          'nextCursor': 7,
          'total': null,
        }, (value, path) => value),
        throwsA(
          isA<OrvanoProtocolException>().having(
            (error) => error.message,
            'message',
            contains(r'$.nextCursor'),
          ),
        ),
      );
    });
  });

  group('walkPages', () {
    ResultPage<int> page(List<int> items, String? next) =>
        ResultPage(items: items, nextCursor: next, total: null);

    test('yields every item of every page, passing each cursor on', () async {
      final cursors = <String?>[];
      final items = await walkPages((cursor) async {
        cursors.add(cursor);
        return switch (cursor) {
          null => page([1, 2], 'c2'),
          'c2' => page([3, 4], 'c3'),
          _ => page([5], null),
        };
      }).toList();
      expect(items, [1, 2, 3, 4, 5]);
      expect(cursors, [null, 'c2', 'c3']);
    });

    test('fetches nothing until listened to, and no page after a cancel', () async {
      var fetched = 0;
      final stream = walkPages((cursor) async {
        fetched++;
        return page([fetched * 10, fetched * 10 + 1], 'c$fetched');
      });
      expect(fetched, 0);
      final taken = await stream.take(2).toList();
      await Future<void>.delayed(Duration.zero);
      expect(taken, [10, 11]);
      expect(fetched, 1);
    });

    test('ends the stream with the error of a failed page', () async {
      final stream = walkPages<int>((cursor) async {
        if (cursor == null) return page([1], 'c2');
        throw StateError('the server went away');
      });
      await expectLater(
        stream,
        emitsInOrder([1, emitsError(isA<StateError>())]),
      );
    });
  });
}
