// Spec 0004 DQA-5, MDL-1 to MDL-4: generated models and the JSON readers,
// over the fixture contract's WidgetResponse. Hand written.
import 'package:orvano_server/orvano_server.dart';
import 'package:orvano_server/src/core/retry.dart';
import 'package:test/test.dart';

Map<String, Object?> _widget() => {
  'id': '0192f0c1-7b9e-7c43-9a51-3f0e8d2b6a10',
  'name': 'first',
  'color': 'dark_green',
  'nickname': null,
  'tags': ['a', 'b'],
  'labels': {'team': 'core'},
  'size': 5,
  'owner': {'userId': '0192f0c1-0000-7000-8000-000000000002', 'kind': 'team'},
  'createdAt': '2026-10-22T09:14:03.120558Z',
  'version': 'AAAAAAAAAAE',
};

void main() {
  group('generated models', () {
    test('read every field and write the same JSON back', () {
      final widget = WidgetResponse.fromJson(_widget());
      expect(widget.color, WidgetColor.darkGreen);
      expect(widget.owner?.kind, WidgetOwnerResponseKind.team);
      expect(widget.createdAt, DateTime.utc(2026, 10, 22, 9, 14, 3, 120, 558));
      expect(widget.toJson(), _widget());
    });

    test('ignore unknown fields', () {
      final json = {
        ..._widget(),
        'addedLater': {'nested': true},
      };
      expect(WidgetResponse.fromJson(json), WidgetResponse.fromJson(_widget()));
    });

    test('read an unknown enum value as unknown and keep it for toJson', () {
      final json = {..._widget(), 'color': 'purple'};
      final widget = WidgetResponse.fromJson(json);
      expect(widget.color, WidgetColor.unknown);
      expect(widget.toJson()['color'], 'purple');
      expect(widget.copyWith(color: WidgetColor.red).toJson()['color'], 'red');
      expect(() => WidgetColor.unknown.toJson(), throwsArgumentError);
    });

    test('report a missing or mistyped field by its path, never its value', () {
      expect(
        () => WidgetResponse.fromJson({..._widget()}..remove('name')),
        throwsA(
          isA<OrvanoProtocolException>().having(
            (e) => e.path,
            'path',
            r'$.name',
          ),
        ),
      );
      expect(
        () => WidgetResponse.fromJson({
          ..._widget(),
          'owner': {'userId': 42, 'kind': 'team'},
        }),
        throwsA(
          isA<OrvanoProtocolException>()
              .having((e) => e.path, 'path', r'$.owner.userId')
              .having((e) => e.message, 'message', isNot(contains('42'))),
        ),
      );
      expect(
        () => WidgetResponse.fromJson({
          ..._widget(),
          'tags': ['a', 7],
        }),
        throwsA(
          isA<OrvanoProtocolException>().having(
            (e) => e.path,
            'path',
            r'$.tags[1]',
          ),
        ),
      );
    });

    test('read whole numbers the same on the VM and the web', () {
      expect(WidgetResponse.fromJson({..._widget(), 'size': 5.0}).size, 5);
      expect(
        () => WidgetResponse.fromJson({..._widget(), 'size': 5.5}),
        throwsA(isA<OrvanoProtocolException>()),
      );
    });

    test('read a timestamp without an offset as UTC', () {
      final widget = WidgetResponse.fromJson({
        ..._widget(),
        'createdAt': '2026-10-22T09:14:03',
      });
      expect(widget.createdAt.isUtc, isTrue);
      expect(widget.createdAt.hour, 9);
      final offset = WidgetResponse.fromJson({
        ..._widget(),
        'createdAt': '2026-10-22T11:14:03+02:00',
      });
      expect(offset.createdAt, DateTime.utc(2026, 10, 22, 9, 14, 3));
    });

    test('compare by value, lists and maps deeply', () {
      final a = WidgetResponse.fromJson(_widget());
      final b = WidgetResponse.fromJson(_widget());
      expect(a, b);
      expect(a.hashCode, b.hashCode);
      expect(a.copyWith(tags: ['c']), isNot(a));
      expect(a.copyWith(name: 'second').name, 'second');
      expect(a.copyWith(name: 'second').id, a.id);
    });
  });

  group('parseRetryAfter', () {
    final now = DateTime.utc(2026, 10, 23, 10);

    test('reads seconds and HTTP dates', () {
      expect(parseRetryAfter('2', now), const Duration(seconds: 2));
      expect(
        parseRetryAfter('Fri, 23 Oct 2026 10:00:05 GMT', now),
        const Duration(seconds: 5),
      );
      expect(
        parseRetryAfter('Fri, 23 Oct 2026 09:00:00 GMT', now),
        Duration.zero,
      );
    });

    test('ignores what it can not read', () {
      for (final value in [
        null,
        '',
        'soon',
        '-1',
        'Fri, 23 Foo 2026 10:00:05 GMT',
      ]) {
        expect(parseRetryAfter(value, now), isNull, reason: '$value');
      }
    });
  });

  test('OrvanoErrorKind follows the status table (spec 0014 KND-2)', () {
    final table = {
      400: 'validation',
      401: 'unauthenticated',
      403: 'forbidden',
      404: 'not_found',
      408: 'timeout',
      409: 'conflict',
      412: 'conflict',
      413: 'validation',
      415: 'validation',
      418: 'server',
      422: 'validation',
      428: 'conflict',
      429: 'rate_limited',
      500: 'server',
      502: 'server',
      503: 'server',
    };
    for (final MapEntry(key: status, value: kind) in table.entries) {
      expect(
        OrvanoErrorKind.fromStatus(status).wireName,
        kind,
        reason: '$status',
      );
    }
  });
}
