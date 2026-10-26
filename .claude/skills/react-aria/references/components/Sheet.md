# Sheet

A Sheet is a swipeable overlay that slides in from the edge of the viewport.

## Vanilla CSS example

```tsx
import {useState} from 'react';
import {SheetTrigger} from 'react-aria-components/Sheet';
import {Sheet, Heading} from 'vanilla-starter/Sheet';
import {Button} from 'vanilla-starter/Button';
import {Minus, Plus, ShoppingCart} from 'lucide-react';

function Example(props) {
  let [items, setItems] = useState([
    {name: 'Pork Bao', detail: 'Steamed · 2 pieces', emoji: '🥟', price: 4.5, qty: 2},
    {name: 'Spicy Ramen', detail: 'Extra spicy · Soft egg', emoji: '🍜', price: 13, qty: 1},
    {name: 'Mango Boba', detail: 'Less ice · 50% sweet', emoji: '🧋', price: 5.75, qty: 1}
  ]);
  let update = (name, delta) => setItems(items => items
    .map(item => item.name === name ? {...item, qty: item.qty + delta} : item)
    .filter(item => item.qty > 0));
  let subtotal = items.reduce((sum, item) => sum + item.price * item.qty, 0);
  let delivery = items.length ? 2.99 : 0;

  return (
    <SheetTrigger>
      <Button>
        <ShoppingCart size={18} />
        <span>Cart ({items.reduce((n, item) => n + item.qty, 0)})</span>
      </Button>
      {/*- begin highlight -*/}
      <Sheet {...props}>
      {/*- end highlight -*/}
        {/*- begin collapse -*/}
        <div style={{display: 'flex', flexDirection: 'column', minHeight: '100%'}}>
          <Heading slot="title">Your order</Heading>
          <p style={{margin: '0 0 20px', color: 'var(--gray-1000)'}}>Delivering from Bao Bros · 25–35 min</p>
          <div style={{display: 'flex', flexDirection: 'column', gap: 'var(--spacing-4)', flexGrow: 1}}>
            {items.map(item => (
              <div key={item.name} style={{display: 'flex', alignItems: 'center', gap: 'var(--spacing-3)'}}>
                <span style={{width: 'calc(15 * var(--spacing))', height: 'calc(15 * var(--spacing))', borderRadius: 12, background: 'var(--gray-200)', fontSize: 28, display: 'flex', alignItems: 'center', justifyContent: 'center', flexShrink: 0}}>{item.emoji}</span>
                <div style={{display: 'flex', alignItems: 'center', columnGap: 'var(--spacing-3)', rowGap: 4, flexWrap: 'wrap', flexGrow: 1}}>
                  <div style={{flexGrow: 100, minWidth: 0}}>
                    <div style={{fontWeight: 600}}>{item.name}</div>
                    <div style={{fontSize: 12, color: 'var(--gray-1000)'}}>{item.detail}</div>
                  </div>
                  <div style={{display: 'flex', alignItems: 'center', gap: 4, flexGrow: 1}}>
                    <Button aria-label={`Remove one ${item.name}`} variant="secondary" onPress={() => update(item.name, -1)}><Minus size={16} /></Button>
                    <span style={{minWidth: 20, textAlign: 'center'}}>{item.qty}</span>
                    <Button aria-label={`Add one ${item.name}`} variant="secondary" onPress={() => update(item.name, 1)}><Plus size={16} /></Button>
                    <span style={{minWidth: '6ch', textAlign: 'end', flexGrow: 1}}>${(item.price * item.qty).toFixed(2)}</span>
                  </div>
                </div>
              </div>
            ))}
          </div>
          <div style={{display: 'flex', flexDirection: 'column', gap: 4, margin: '20px 0', paddingTop: 16, borderTop: '1px solid var(--gray-300)'}}>
            <div style={{display: 'flex', justifyContent: 'space-between'}}><span>Subtotal</span><span>${subtotal.toFixed(2)}</span></div>
            <div style={{display: 'flex', justifyContent: 'space-between'}}><span>Delivery fee</span><span>${delivery.toFixed(2)}</span></div>
            <div style={{display: 'flex', justifyContent: 'space-between', fontWeight: 600}}><span>Total</span><span>${(subtotal + delivery).toFixed(2)}</span></div>
          </div>
          <Button slot="close" style={{width: '100%'}}>Place order</Button>
        </div>
        {/*- end collapse -*/}
      </Sheet>
    </SheetTrigger>
  );
}
```

### Sheet.tsx

```tsx
'use client';
import {type DialogProps, Heading} from 'react-aria-components/Dialog';
import {
  Sheet as RACSheet,
  SheetBackdrop,
  SheetContent,
  SheetOverlay,
  type SheetOverlayProps,
  type SheetProps as AriaSheetProps
} from 'react-aria-components/Sheet';
import './Sheet.css';

export interface SheetProps
  extends
    Omit<SheetOverlayProps, 'children'>,
    Pick<AriaSheetProps, 'swipeAnimation' | 'swipeAnimationRange'> {
  /** The contents of the sheet. Rendered inside a Dialog. */
  children?: DialogProps['children'];
  /**
   * Whether to add padding to the sheet so that it appears to continue outside the viewport.
   *
   * @default true
   */
  overscrollPadding?: boolean;
}

export function Sheet({children, ...props}: SheetProps) {
  let {snapPoints, preventDismissal} = props;
  return (
    <SheetOverlay {...props} style={undefined}>
      {/* The backdrop fades in as the sheet slides up. When snap points are used, it stays hidden
        until the sheet is dragged past the last snap point. */}
      <SheetBackdrop
        swipeAnimation="sheet-backdrop"
        swipeAnimationRange={snapPoints ? {start: snapPoints.length - 1} : undefined}
      />
      {/* overscrollPadding makes the sheet appear to continue past the edge of the screen when overscrolled.
        The scaleBack animation scales a parent sheet backward when a child sheet is opened on top of it. */}
      <RACSheet
        overscrollPadding={props.overscrollPadding ?? true}
        swipeAnimation={props.swipeAnimation}
        swipeAnimationRange={props.swipeAnimationRange}
        stackAnimation="sheet-scale-back"
        style={props.style}>
        {!preventDismissal && <div className="sheet-handle" />}
        <SheetContent>{children}</SheetContent>
      </RACSheet>
    </SheetOverlay>
  );
}

export {Heading};

```

### Sheet.css

```css
@import './theme.css';

.react-aria-Sheet {
  background: var(--overlay-background);
  color: var(--text-color);
  outline: 1px solid var(--overlay-border);
  box-shadow: var(--popover-shadow);
  font-family: system-ui;
  font-size: var(--font-size);
  height: auto;
  max-height: calc(100% - 32px);
  width: calc(100% - var(--spacing-3));
  max-width: 800px;
  margin: var(--spacing-2);
  border-radius: 32px;
  overflow: clip;
  z-index: 1;
  /* Establish a 3D space so stacked sheets can scale backward along the z-axis. */
  transform: perspective(1000px);
  will-change: transform;

  &[data-position='left'],
  &[data-position='right'] {
    width: 340px;
    height: calc(100% - var(--spacing-3));
    max-height: unset;
  }

  &[data-position='bottom'],
  &[data-position='center'] {
    transform-origin: center -150px;
  }

  &[data-position='top'] {
    transform-origin: center 150px;
  }

  &[data-position='left'] {
    transform-origin: 150px center;
  }

  &[data-position='right'] {
    transform-origin: -150px center;
  }

  .sheet-handle {
    border-radius: 6px;
    background: var(--gray-300);
    position: absolute;
    z-index: 2;
    transition: opacity 300ms;
  }

  &[data-swipe-direction='bottom'] .sheet-handle,
  &[data-swipe-direction='vertical'] .sheet-handle {
    top: 12px;
    left: 50%;
    transform: translateX(-50%);
    width: 80px;
    height: 6px;
  }
  &[data-swipe-direction='top'] .sheet-handle {
    bottom: 12px;
    left: 50%;
    transform: translateX(-50%);
    width: 80px;
    height: 6px;
  }
  &[data-swipe-direction='left'] .sheet-handle,
  &[data-swipe-direction='horizontal'] .sheet-handle {
    right: 12px;
    top: 50%;
    transform: translateY(-50%);
    height: 80px;
    width: 6px;
  }
  &[data-swipe-direction='right'] .sheet-handle {
    left: 12px;
    top: 50%;
    transform: translateY(-50%);
    height: 80px;
    width: 6px;
  }

  &[data-has-descendants] .sheet-handle {
    opacity: 0;
  }
}

.react-aria-SheetBackdrop {
  /* Only the bottom-most sheet in a stack dims the page behind it. */
  &[data-stack-index='0'] {
    background: rgb(0 0 0 / 0.4);
  }
}

.react-aria-SheetContent {
  padding: var(--spacing-8);
  padding-bottom: calc(var(--spacing-8) + var(--sheet-scroll-padding-y));
  mask-image: linear-gradient(
    to bottom,
    transparent 0px var(--spacing-4),
    black var(--spacing-8) calc(100% - var(--spacing-8)),
    transparent
  );
  box-sizing: border-box;
  height: 100%;
  outline: none;
  /* Only scroll the inner content once the sheet is fully expanded. At a partial detent a swipe on
     the content chains out to the sheet's scroll container instead, expanding the sheet (like iOS). */
  overflow: hidden;
  overscroll-behavior: auto;
  scrollbar-gutter: stable;

  .react-aria-Heading[slot='title'] {
    margin-top: 0;
    margin-bottom: var(--spacing-1);
    font-size: 1.3rem;
  }
}

.react-aria-SheetOverlay[data-expanded] .react-aria-SheetContent {
  overflow: auto;
}

@keyframes sheet-backdrop {
  from {
    opacity: 0;
  }

  to {
    opacity: 1;
  }
}

@keyframes sheet-scale-back {
  to {
    transform: translateZ(-120px);
  }
}

```

## Tailwind example

```tsx
import {useState} from 'react';
import {SheetTrigger} from 'react-aria-components/Sheet';
import {Sheet} from 'tailwind-starter/Sheet';
import {Heading} from 'tailwind-starter/Dialog';
import {Button} from 'tailwind-starter/Button';
import {Minus, Plus, ShoppingCart} from 'lucide-react';

function Example(props) {
  let [items, setItems] = useState([
    {name: 'Pork Bao', detail: 'Steamed · 2 pieces', emoji: '🥟', price: 4.5, qty: 2},
    {name: 'Spicy Ramen', detail: 'Extra spicy · Soft egg', emoji: '🍜', price: 13, qty: 1},
    {name: 'Mango Boba', detail: 'Less ice · 50% sweet', emoji: '🧋', price: 5.75, qty: 1}
  ]);
  let update = (name, delta) => setItems(items => items
    .map(item => item.name === name ? {...item, qty: item.qty + delta} : item)
    .filter(item => item.qty > 0));
  let subtotal = items.reduce((sum, item) => sum + item.price * item.qty, 0);
  let delivery = items.length ? 2.99 : 0;

  return (
    <SheetTrigger>
      <Button>
        <ShoppingCart size={18} />
        <span>Cart ({items.reduce((n, item) => n + item.qty, 0)})</span>
      </Button>
      {/*- begin highlight -*/}
      <Sheet {...props}>
      {/*- end highlight -*/}
      {/*- begin collapse -*/}
        <div className="flex flex-col min-h-full">
          <Heading slot="title" className="text-xl font-semibold mt-0 mb-0.5">Your order</Heading>
          <p className="mt-0 mb-5 text-sm text-neutral-600 dark:text-neutral-400">Delivering from Bao Bros · 25–35 min</p>
          <div className="flex flex-col gap-4 flex-1">
            {items.map(item => (
              <div key={item.name} className="flex items-center gap-3">
                <span className="w-14 h-14 rounded-xl bg-neutral-200 dark:bg-neutral-700 text-3xl flex items-center justify-center shrink-0">{item.emoji}</span>
                <div className="flex items-center flex-wrap gap-x-3 gap-y-1 grow">
                  <div className="grow-100 min-w-0">
                    <div className="text-sm font-semibold">{item.name}</div>
                    <div className="text-xs text-neutral-600 dark:text-neutral-400">{item.detail}</div>
                  </div>
                  <div className="flex items-center gap-1 grow">
                    <Button aria-label={`Remove one ${item.name}`} variant="secondary" onPress={() => update(item.name, -1)}><Minus size={16} /></Button>
                    <span className="min-w-5 text-center">{item.qty}</span>
                    <Button aria-label={`Add one ${item.name}`} variant="secondary" onPress={() => update(item.name, 1)}><Plus size={16} /></Button>
                    <span className="min-w-[6ch] text-sm text-end grow">${(item.price * item.qty).toFixed(2)}</span>
                  </div>
                </div>
              </div>
            ))}
          </div>
          <div className="flex flex-col gap-1 my-5 pt-4 text-sm border-t border-neutral-300 dark:border-neutral-700">
            <div className="flex justify-between"><span>Subtotal</span><span>${subtotal.toFixed(2)}</span></div>
            <div className="flex justify-between"><span>Delivery fee</span><span>${delivery.toFixed(2)}</span></div>
            <div className="flex justify-between font-semibold"><span>Total</span><span>${(subtotal + delivery).toFixed(2)}</span></div>
          </div>
          <Button slot="close" className="w-full">Place order</Button>
        </div>
        {/*- end collapse -*/}
      </Sheet>
    </SheetTrigger>
  );
}
```

### Sheet.tsx

```tsx
'use client';
import React from 'react';
import {type DialogProps} from 'react-aria-components/Dialog';
import {
  Sheet as RACSheet,
  SheetBackdrop,
  SheetContent,
  SheetOverlay,
  type SheetOverlayProps
} from 'react-aria-components/Sheet';
import {tv} from 'tailwind-variants';
import {composeTailwindRenderProps} from './utils';
import './Sheet.css';

// Only the bottom-most sheet in a stack dims the page behind it.
const backdropStyles = tv({
  base: "data-[stack-index='0']:bg-black/40"
});

const sheetStyles = tv({
  // Establish a 3D space (perspective) so stacked sheets can scale backward along the z-axis.
  base: 'font-sans text-neutral-700 dark:text-neutral-300 bg-white dark:bg-neutral-800 forced-colors:bg-[Canvas] outline-hidden overflow-clip rounded-4xl shadow-2xl border border-black/10 dark:border-white/10 z-[1] will-change-transform [transform:perspective(1000px)]',
  variants: {
    position: {
      bottom: 'w-[calc(100%-1rem)] max-w-[800px] max-h-[calc(100%_-_32px)] origin-[center_-150px]',
      top: 'w-[calc(100%-1rem)] max-w-[800px] max-h-[calc(100%_-_32px)] origin-[center_150px]',
      center: 'w-[calc(100%-1rem)] max-w-[800px] max-h-[calc(100%_-_32px)] origin-[center_-150px]',
      left: 'w-[300px] h-[calc(100%-1rem)] origin-[150px_center]',
      right: 'w-[300px] h-[calc(100%-1rem)] origin-[-150px_center]'
    }
  },
  defaultVariants: {
    position: 'bottom'
  }
});

// Only scroll the inner content once the sheet is fully expanded. At a partial detent a swipe on the
// content chains out to the sheet's scroll container instead, expanding the sheet (like iOS).
const contentStyles = tv({
  base: 'p-6 pb-[calc(var(--spacing-6) + var(--sheet-scroll-padding-y))] box-border h-full outline-hidden overscroll-auto scrollbar-gutter-stable overflow-hidden group-data-[expanded]/sheet:overflow-auto'
});

export interface SheetProps extends Omit<SheetOverlayProps, 'children'> {
  /** The contents of the sheet. Rendered inside a Dialog. */
  children?: DialogProps['children'];
  /**
   * Whether to add padding to the sheet so that it appears to continue outside the viewport.
   *
   * @default true
   */
  overscrollPadding?: boolean;
}

export function Sheet({children, ...props}: SheetProps) {
  let {snapPoints} = props;
  return (
    <SheetOverlay {...props} className={composeTailwindRenderProps(props.className, 'group/sheet')}>
      {/* The backdrop fades in as the sheet slides up. When snap points are used, it stays hidden
        until the sheet is dragged past the last snap point. */}
      <SheetBackdrop
        className={backdropStyles()}
        swipeAnimation="sheet-backdrop"
        swipeAnimationRange={snapPoints ? {start: snapPoints.length - 1} : undefined}
      />
      {/* overscrollPadding makes the sheet appear to continue past the edge of the screen when overscrolled.
        The scaleBack animation scales a parent sheet backward when a child sheet is opened on top of it. */}
      <RACSheet
        overscrollPadding={props.overscrollPadding ?? true}
        stackAnimation="sheet-scale-back"
        className={({position}) => sheetStyles({position})}>
        <SheetContent className={contentStyles()}>{children}</SheetContent>
      </RACSheet>
    </SheetOverlay>
  );
}

```

### Sheet.css

```css
@keyframes sheet-backdrop {
  from {
    opacity: 0;
  }

  to {
    opacity: 1;
  }
}

@keyframes sheet-scale-back {
  to {
    transform: translateZ(-120px);
  }
}

```

## Position

The `position` prop determines which edge of the screen the sheet is anchored to. `swipeDirection` controls the direction it can be swiped to dismiss, which defaults to the same as `position`. Use `start` or `end` to build a navigation drawer, `bottom` for a mobile sheet, or `top` for a notification banner. `start` and `end` are automatically mirrored in right-to-left locales, while `left` and `right` are always physical.

```tsx
import {SheetTrigger} from 'react-aria-components/Sheet';
import {Sheet, Heading} from 'vanilla-starter/Sheet';
import {Button} from 'vanilla-starter/Button';
import {Home, Compass, Library, Settings, LogOut} from 'lucide-react';

function Example() {
  let items = [
    {label: 'Home', icon: <Home size={20} />},
    {label: 'Explore', icon: <Compass size={20} />},
    {label: 'Library', icon: <Library size={20} />},
    {label: 'Settings', icon: <Settings size={20} />}
  ];
  return (
    <SheetTrigger>
      <Button variant="secondary">Open menu</Button>
      {/*- begin highlight -*/}
      <Sheet position="start">
      {/*- end highlight -*/}
        <div style={{display: 'flex', flexDirection: 'column', height: '100%', alignItems: 'start'}}>
          <Heading slot="title" style={{padding: '0 var(--spacing-3)', marginBottom: 16}}>Acme Music</Heading>
          <nav style={{display: 'flex', flexDirection: 'column', gap: 4, flexGrow: 1, width: '100%'}}>
            {items.map(item => (
              <Button key={item.label} slot="close" variant="quiet" style={{justifyContent: 'start', gap: 12}}>
                {item.icon}
                <span>{item.label}</span>
              </Button>
            ))}
          </nav>
          <Button slot="close" variant="secondary" style={{gap: 12, marginTop: 'auto'}}>
            <LogOut size={20} />
            <span>Sign out</span>
          </Button>
        </div>
      </Sheet>
    </SheetTrigger>
  );
}
```

## Snap points

By default, a sheet opens to reveal all of its content. Provide the `snapPoints` prop to make it stop partially open instead. Each value is the amount of the sheet that is visible, either as a number of pixels or a CSS length. Percentages are relative to the size of the sheet. The sheet opens at the first snap point, and can be dragged up to its full height or swiped down to dismiss.

```tsx
import {SheetTrigger} from 'react-aria-components/Sheet';
import {Sheet, Heading} from 'vanilla-starter/Sheet';
import {Button} from 'vanilla-starter/Button';
import {Star, MapPin, Clock, Phone} from 'lucide-react';
import {Map} from './Map';

let reviews = [
  {name: 'Maya R.', date: '2 days ago', text: 'The espresso was smooth and the staff was lovely. A great spot for a quiet morning.'},
  {name: 'Jordan K.', date: '1 week ago', text: 'Really good pour-over and plenty of seating. It gets busy around lunch, but the line moves quickly.'},
  {name: 'Avery L.', date: '2 weeks ago', text: 'The iced coffee was bright and refreshing, and the pastries were excellent. I will definitely be back.'},
  {name: 'Chris T.', date: '1 month ago', text: 'Beautiful space with consistently good coffee. The cappuccino had the perfect amount of foam.'},
  {name: 'Samira P.', date: '1 month ago', text: 'Friendly service, fast Wi-Fi, and a great neighborhood atmosphere. One of my favorite coffee shops in Port Marlow.'}
];

function Example() {
  return (
    <SheetTrigger>
      <Button>Show place</Button>
      {/*- begin highlight -*/}
      <Sheet position="bottom" snapPoints={[280]}>
      {/*- end highlight -*/}
        <Heading slot="title" style={{marginBottom: 4}}>Harborlight Coffee</Heading>
        <div style={{display: 'flex', alignItems: 'center', gap: 8, color: 'var(--gray-1000)', marginBottom: 32}}>
          <Star size={16} fill="currentColor" style={{color: '#ff9500'}} />
          <strong>4.6</strong> · Coffee shop · $$
        </div>
        <div style={{display: 'flex', flexDirection: 'column', gap: 16, alignItems: 'start'}}>
          <div style={{display: 'flex', gap: 12, alignItems: 'center'}}><MapPin size={18} /> 300 Harbor St, Port Marlow</div>
          <div style={{display: 'flex', gap: 12, alignItems: 'center'}}><Clock size={18} /> Open until 6:00 PM</div>
          <div style={{display: 'flex', gap: 12, alignItems: 'center'}}><Phone size={18} /> (510) 555-0142</div>
          <p>Drag this sheet up to see more, or swipe down to dismiss. Snap points let you preview the most relevant content first, then expand for the full story.</p>
          <Map />
          <Button slot="close">Get directions</Button>
          <h3 style={{margin: '8px 0 0'}}>Reviews</h3>
          <div style={{display: 'flex', flexDirection: 'column', gap: 16, width: '100%'}}>
            {reviews.map(review => (
              <div key={review.name} style={{padding: 16, border: '1px solid var(--gray-300)', borderRadius: 12}}>
                <div style={{display: 'flex', justifyContent: 'space-between', gap: 8, marginBottom: 8}}>
                  <strong>{review.name}</strong>
                  <span style={{color: 'var(--gray-700)', fontSize: 14}}>{review.date}</span>
                </div>
                <div style={{display: 'flex', gap: 2, color: '#ff9500', marginBottom: 8}}>
                  {Array.from({length: 5}, (_, i) => <Star key={i} size={14} fill="currentColor" />)}
                </div>
                <div>{review.text}</div>
              </div>
            ))}
          </div>
        </div>
      </Sheet>
    </SheetTrigger>
  );
}
```

## Animation

Use the `swipeAnimation` prop on `Sheet` and `SheetBackdrop` to define a CSS keyframe animation that is synchronized with the swipe gesture and entry/exit animations. `swipeAnimationRange` specifies the snap points between which the animation plays.

```tsx
import {SheetTrigger} from 'react-aria-components/Sheet';
import {Sheet, Heading} from 'vanilla-starter/Sheet';
import {Button} from 'vanilla-starter/Button';

function Example() {
  return (
    <SheetTrigger>
      <Button>Settings</Button>
      {/* By default, Sheet.tsx applies a fade transition to the backdrop.
          This example adds a custom transform animation to the sheet. */}
      <style>{`
      @keyframes sheet-transform {
        from {
          transform-origin: top right;
          transform: skew(10deg) scale(0.8);
        }

        to {
          transform: skew(0deg) scale(1);
        }
      }`}</style>
      {/*- begin highlight -*/}
      <Sheet swipeAnimation="sheet-transform">
      {/*- end highlight -*/}
        <Heading slot="title">Sheet</Heading>
        <p style={{marginTop: 0}}>This sheet has a custom swipe animation.</p>
        <div style={{height: 300, width: '100%', background: 'var(--gray-200)', borderRadius: 16, marginBottom: 16}} />
        <Button slot="close">Close</Button>
      </Sheet>
    </SheetTrigger>
  );
}
```

## Stacking

Sheets can be nested to drill deeper into a flow. Set `stackAnimation` prop to a CSS keyframe animation to scale the parent sheet backward when a child is opened, synchronized with the swipe gesture. The `Sheet` component in this example wires this up automatically.

```tsx
import {SheetTrigger} from 'react-aria-components/Sheet';
import {Sheet, Heading} from 'vanilla-starter/Sheet';
import {Button} from 'vanilla-starter/Button';
import {Form} from 'vanilla-starter/Form';
import {TextField} from 'vanilla-starter/TextField';
import {RadioGroup, Radio} from 'vanilla-starter/RadioGroup';
import {CheckboxGroup} from 'vanilla-starter/CheckboxGroup';
import {Checkbox} from 'vanilla-starter/Checkbox';
import {ChevronRight} from 'lucide-react';

function Example() {
  return (
    <SheetTrigger>
      <Button>Settings</Button>
      <Sheet style={{height: 550}}>
        <Heading slot="title">Settings</Heading>
        <p style={{marginTop: 0, marginBottom: 32}}>Manage your account and preferences.</p>
        <Form>
          <TextField label="Homepage" defaultValue="react-aria.adobe.com" />
          <RadioGroup label="Theme" defaultValue="auto">
            <Radio value="auto">Auto</Radio>
            <Radio value="light">Light</Radio>
            <Radio value="dark">Dark</Radio>
          </RadioGroup>
          {/*- begin highlight -*/}
          <SheetTrigger>
            <Button variant="secondary">
              <span>Account</span>
              <ChevronRight size={18} />
            </Button>
            <Sheet style={{height: 550}}>
            {/*- end highlight -*/}
              <Heading slot="title">Account</Heading>
              <p style={{marginTop: 0, marginBottom: 32}}>Swipe this sheet away to return to Settings.</p>
              <Form>
                <TextField label="Name" defaultValue="Devon Govett" />
                <TextField label="Username" defaultValue="@devongovett" />
                <TextField label="Email" defaultValue="devon@example.com" />
                <div style={{display: 'flex', justifyContent: 'space-between'}}>
                  <Button slot="close">Save</Button>
                  <SheetTrigger>
                    <Button variant="secondary">
                      <span>Advanced</span>
                      <ChevronRight size={18} />
                    </Button>
                    <Sheet style={{height: 550}}>
                      <Heading slot="title">Advanced</Heading>
                      <p style={{marginTop: 0, marginBottom: 32}}>Swipe this sheet away to return to Account.</p>
                      <Form>
                        <CheckboxGroup label="Notification settings" defaultValue={['account', 'dms']}>
                          <Checkbox value="account">Account activity</Checkbox>
                          <Checkbox value="mentions">Mentions</Checkbox>
                          <Checkbox value="dms">Direct message</Checkbox>
                          <Checkbox value="marketing">Marketing emails</Checkbox>
                        </CheckboxGroup>
                        <Button slot="close">Save</Button>
                      </Form>
                    </Sheet>
                  </SheetTrigger>
                </div>
              </Form>
            </Sheet>
          </SheetTrigger>
        </Form>
      </Sheet>
    </SheetTrigger>
  );
}
```

## API

```tsx
<SheetTrigger>
  <Button />
  <SheetOverlay>
    <SheetBackdrop />
    <Sheet>
      <SheetContent>
        <Heading slot="title" />
        <Text slot="description" />
        <Button slot="close" />
      </SheetContent>
    </Sheet>
  </SheetOverlay>
</SheetTrigger>
```

### SheetTrigger

| Name | Type | Default | Description |
|------|------|---------|-------------|
| `children` | `React.ReactNode` | — |  |
| `defaultOpen` | `boolean | undefined` | — | Whether the overlay is open by default (uncontrolled). |
| `isOpen` | `boolean | undefined` | — | Whether the overlay is open by default (controlled). |
| `onOpenChange` | `((isOpen: boolean) => void) | undefined` | — | Handler that is called when the overlay's open state changes. |

### SheetOverlay

| Name | Type | Default | Description |
|------|------|---------|-------------|
| `children` | `ChildrenOrFunction<SheetRenderProps>` | — | The children of the component. A function may be provided to alter the children based on component state. |
| `className` | `ClassNameOrFunction<SheetRenderProps> | undefined` | 'react-aria-SheetOverlay' | The CSS [className](https://developer.mozilla.org/en-US/docs/Web/API/Element/className) for the element. A function may be provided to compute the class based on component state. |
| `defaultOpen` | `boolean | undefined` | — | Whether the overlay is open by default (uncontrolled). |
| `dir` | `string | undefined` | — |  |
| `hidden` | `boolean | undefined` | — |  |
| `inert` | `boolean | undefined` | — |  |
| `isOpen` | `boolean | undefined` | — | Whether the overlay is open by default (controlled). |
| `lang` | `string | undefined` | — |  |
| `onAnimationEnd` | `React.AnimationEventHandler<HTMLDivElement> | undefined` | — |  |
| `onAnimationEndCapture` | `React.AnimationEventHandler<HTMLDivElement> | undefined` | — |  |
| `onAnimationIteration` | `React.AnimationEventHandler<HTMLDivElement> | undefined` | — |  |
| `onAnimationIterationCapture` | `React.AnimationEventHandler<HTMLDivElement> | undefined` | — |  |
| `onAnimationStart` | `React.AnimationEventHandler<HTMLDivElement> | undefined` | — |  |
| `onAnimationStartCapture` | `React.AnimationEventHandler<HTMLDivElement> | undefined` | — |  |
| `onAuxClick` | `React.MouseEventHandler<HTMLDivElement> | undefined` | — |  |
| `onAuxClickCapture` | `React.MouseEventHandler<HTMLDivElement> | undefined` | — |  |
| `onClick` | `React.MouseEventHandler<HTMLDivElement> | undefined` | — |  |
| `onClickCapture` | `React.MouseEventHandler<HTMLDivElement> | undefined` | — |  |
| `onContextMenu` | `React.MouseEventHandler<HTMLDivElement> | undefined` | — |  |
| `onContextMenuCapture` | `React.MouseEventHandler<HTMLDivElement> | undefined` | — |  |
| `onDoubleClick` | `React.MouseEventHandler<HTMLDivElement> | undefined` | — |  |
| `onDoubleClickCapture` | `React.MouseEventHandler<HTMLDivElement> | undefined` | — |  |
| `onGotPointerCapture` | `React.PointerEventHandler<HTMLDivElement> | undefined` | — |  |
| `onGotPointerCaptureCapture` | `React.PointerEventHandler<HTMLDivElement> | undefined` | — |  |
| `onLostPointerCapture` | `React.PointerEventHandler<HTMLDivElement> | undefined` | — |  |
| `onLostPointerCaptureCapture` | `React.PointerEventHandler<HTMLDivElement> | undefined` | — |  |
| `onMouseDown` | `React.MouseEventHandler<HTMLDivElement> | undefined` | — |  |
| `onMouseDownCapture` | `React.MouseEventHandler<HTMLDivElement> | undefined` | — |  |
| `onMouseEnter` | `React.MouseEventHandler<HTMLDivElement> | undefined` | — |  |
| `onMouseLeave` | `React.MouseEventHandler<HTMLDivElement> | undefined` | — |  |
| `onMouseMove` | `React.MouseEventHandler<HTMLDivElement> | undefined` | — |  |
| `onMouseMoveCapture` | `React.MouseEventHandler<HTMLDivElement> | undefined` | — |  |
| `onMouseOut` | `React.MouseEventHandler<HTMLDivElement> | undefined` | — |  |
| `onMouseOutCapture` | `React.MouseEventHandler<HTMLDivElement> | undefined` | — |  |
| `onMouseOver` | `React.MouseEventHandler<HTMLDivElement> | undefined` | — |  |
| `onMouseOverCapture` | `React.MouseEventHandler<HTMLDivElement> | undefined` | — |  |
| `onMouseUp` | `React.MouseEventHandler<HTMLDivElement> | undefined` | — |  |
| `onMouseUpCapture` | `React.MouseEventHandler<HTMLDivElement> | undefined` | — |  |
| `onOpenChange` | `((isOpen: boolean) => void) | undefined` | — | Handler that is called when the overlay's open state changes. |
| `onPointerCancel` | `React.PointerEventHandler<HTMLDivElement> | undefined` | — |  |
| `onPointerCancelCapture` | `React.PointerEventHandler<HTMLDivElement> | undefined` | — |  |
| `onPointerDown` | `React.PointerEventHandler<HTMLDivElement> | undefined` | — |  |
| `onPointerDownCapture` | `React.PointerEventHandler<HTMLDivElement> | undefined` | — |  |
| `onPointerEnter` | `React.PointerEventHandler<HTMLDivElement> | undefined` | — |  |
| `onPointerLeave` | `React.PointerEventHandler<HTMLDivElement> | undefined` | — |  |
| `onPointerMove` | `React.PointerEventHandler<HTMLDivElement> | undefined` | — |  |
| `onPointerMoveCapture` | `React.PointerEventHandler<HTMLDivElement> | undefined` | — |  |
| `onPointerOut` | `React.PointerEventHandler<HTMLDivElement> | undefined` | — |  |
| `onPointerOutCapture` | `React.PointerEventHandler<HTMLDivElement> | undefined` | — |  |
| `onPointerOver` | `React.PointerEventHandler<HTMLDivElement> | undefined` | — |  |
| `onPointerOverCapture` | `React.PointerEventHandler<HTMLDivElement> | undefined` | — |  |
| `onPointerUp` | `React.PointerEventHandler<HTMLDivElement> | undefined` | — |  |
| `onPointerUpCapture` | `React.PointerEventHandler<HTMLDivElement> | undefined` | — |  |
| `onScroll` | `React.UIEventHandler<HTMLDivElement> | undefined` | — |  |
| `onScrollCapture` | `React.UIEventHandler<HTMLDivElement> | undefined` | — |  |
| `onTouchCancel` | `React.TouchEventHandler<HTMLDivElement> | undefined` | — |  |
| `onTouchCancelCapture` | `React.TouchEventHandler<HTMLDivElement> | undefined` | — |  |
| `onTouchEnd` | `React.TouchEventHandler<HTMLDivElement> | undefined` | — |  |
| `onTouchEndCapture` | `React.TouchEventHandler<HTMLDivElement> | undefined` | — |  |
| `onTouchMove` | `React.TouchEventHandler<HTMLDivElement> | undefined` | — |  |
| `onTouchMoveCapture` | `React.TouchEventHandler<HTMLDivElement> | undefined` | — |  |
| `onTouchStart` | `React.TouchEventHandler<HTMLDivElement> | undefined` | — |  |
| `onTouchStartCapture` | `React.TouchEventHandler<HTMLDivElement> | undefined` | — |  |
| `onTransitionCancel` | `React.TransitionEventHandler<HTMLDivElement> | undefined` | — |  |
| `onTransitionCancelCapture` | `React.TransitionEventHandler<HTMLDivElement> | undefined` | — |  |
| `onTransitionEnd` | `React.TransitionEventHandler<HTMLDivElement> | undefined` | — |  |
| `onTransitionEndCapture` | `React.TransitionEventHandler<HTMLDivElement> | undefined` | — |  |
| `onTransitionRun` | `React.TransitionEventHandler<HTMLDivElement> | undefined` | — |  |
| `onTransitionRunCapture` | `React.TransitionEventHandler<HTMLDivElement> | undefined` | — |  |
| `onTransitionStart` | `React.TransitionEventHandler<HTMLDivElement> | undefined` | — |  |
| `onTransitionStartCapture` | `React.TransitionEventHandler<HTMLDivElement> | undefined` | — |  |
| `onWheel` | `React.WheelEventHandler<HTMLDivElement> | undefined` | — |  |
| `onWheelCapture` | `React.WheelEventHandler<HTMLDivElement> | undefined` | — |  |
| `position` | `"bottom" | "center" | "end" | "left" | "right" | "start" | "top" | undefined` | 'bottom' | The placement of the sheet on the screen. `start` and `end` are mirrored in right-to-left locales. |
| `preventDismissal` | `boolean | undefined` | false | Whether to prevent the user from closing the sheet by swiping, pressing Escape, or interacting outside it. |
| `render` | `DOMRenderFunction<"div", SheetRenderProps> | undefined` | — | Overrides the default DOM element with a custom render function. This allows rendering existing components with built-in styles and behaviors such as router links, animation libraries, and pre-styled components. Requirements: - You must render the expected element type (e.g. if `<button>` is expected, you cannot render an   `<a>`). - Only a single root DOM element can be rendered (no fragments). - You must pass through props and ref to the underlying DOM element, merging with your own prop   as appropriate. |
| `shouldCloseOnInteractOutside` | `((element: Element) => boolean) | undefined` | — | When user interacts with the argument element outside of the overlay ref, return true if onClose should be called.  This gives you a chance to filter out interaction with elements that should not dismiss the overlay. By default, onClose will always be called on interaction outside the overlay ref. |
| `slot` | `string | null | undefined` | — | A slot name for the component. Slots allow the component to receive props from a parent component. An explicit `null` value indicates that the local props completely override all props received from a parent. |
| `snapPoints` | `(number | string)[] | undefined` | — | Snap points the sheet will stop at, expressed as the amount of the sheet that is visible. Numbers are pixels, and percentages are relative to the size of the sheet. Sheets initially open to the first snap point. |
| `style` | `StyleOrFunction<SheetRenderProps> | undefined` | — | The inline [style](https://developer.mozilla.org/en-US/docs/Web/API/HTMLElement/style) for the element. A function may be provided to compute the style based on component state. |
| `suppressHydrationWarning` | `boolean | undefined` | — | A React-only flag that suppresses the warning React normally logs when an element's server-rendered content doesn't match the client render during hydration. See [React docs](https://react.dev/reference/react-dom/client/hydrateRoot#suppressing-unavoidable-hydration-mismatch-errors). |
| `swipeDirection` | `"bottom" | "end" | "horizontal" | "left" | "right" | "start" | "top" | "vertical" | undefined` | — | The direction the sheet can be swiped. `start` and `end` are mirrored in right-to-left locales. Defaults to the same direction as `position`. |
| `translate` | `"no" | "yes" | undefined` | — |  |

### SheetBackdrop

| Name | Type | Default | Description |
|------|------|---------|-------------|
| `children` | `ChildrenOrFunction<SheetRenderProps>` | — | The children of the component. A function may be provided to alter the children based on component state. |
| `className` | `ClassNameOrFunction<SheetRenderProps> | undefined` | 'react-aria-SheetBackdrop' | The CSS [className](https://developer.mozilla.org/en-US/docs/Web/API/Element/className) for the element. A function may be provided to compute the class based on component state. |
| `dir` | `string | undefined` | — |  |
| `hidden` | `boolean | undefined` | — |  |
| `inert` | `boolean | undefined` | — |  |
| `lang` | `string | undefined` | — |  |
| `onAnimationEnd` | `React.AnimationEventHandler<HTMLDivElement> | undefined` | — |  |
| `onAnimationEndCapture` | `React.AnimationEventHandler<HTMLDivElement> | undefined` | — |  |
| `onAnimationIteration` | `React.AnimationEventHandler<HTMLDivElement> | undefined` | — |  |
| `onAnimationIterationCapture` | `React.AnimationEventHandler<HTMLDivElement> | undefined` | — |  |
| `onAnimationStart` | `React.AnimationEventHandler<HTMLDivElement> | undefined` | — |  |
| `onAnimationStartCapture` | `React.AnimationEventHandler<HTMLDivElement> | undefined` | — |  |
| `onAuxClick` | `React.MouseEventHandler<HTMLDivElement> | undefined` | — |  |
| `onAuxClickCapture` | `React.MouseEventHandler<HTMLDivElement> | undefined` | — |  |
| `onClick` | `React.MouseEventHandler<HTMLDivElement> | undefined` | — |  |
| `onClickCapture` | `React.MouseEventHandler<HTMLDivElement> | undefined` | — |  |
| `onContextMenu` | `React.MouseEventHandler<HTMLDivElement> | undefined` | — |  |
| `onContextMenuCapture` | `React.MouseEventHandler<HTMLDivElement> | undefined` | — |  |
| `onDoubleClick` | `React.MouseEventHandler<HTMLDivElement> | undefined` | — |  |
| `onDoubleClickCapture` | `React.MouseEventHandler<HTMLDivElement> | undefined` | — |  |
| `onGotPointerCapture` | `React.PointerEventHandler<HTMLDivElement> | undefined` | — |  |
| `onGotPointerCaptureCapture` | `React.PointerEventHandler<HTMLDivElement> | undefined` | — |  |
| `onLostPointerCapture` | `React.PointerEventHandler<HTMLDivElement> | undefined` | — |  |
| `onLostPointerCaptureCapture` | `React.PointerEventHandler<HTMLDivElement> | undefined` | — |  |
| `onMouseDown` | `React.MouseEventHandler<HTMLDivElement> | undefined` | — |  |
| `onMouseDownCapture` | `React.MouseEventHandler<HTMLDivElement> | undefined` | — |  |
| `onMouseEnter` | `React.MouseEventHandler<HTMLDivElement> | undefined` | — |  |
| `onMouseLeave` | `React.MouseEventHandler<HTMLDivElement> | undefined` | — |  |
| `onMouseMove` | `React.MouseEventHandler<HTMLDivElement> | undefined` | — |  |
| `onMouseMoveCapture` | `React.MouseEventHandler<HTMLDivElement> | undefined` | — |  |
| `onMouseOut` | `React.MouseEventHandler<HTMLDivElement> | undefined` | — |  |
| `onMouseOutCapture` | `React.MouseEventHandler<HTMLDivElement> | undefined` | — |  |
| `onMouseOver` | `React.MouseEventHandler<HTMLDivElement> | undefined` | — |  |
| `onMouseOverCapture` | `React.MouseEventHandler<HTMLDivElement> | undefined` | — |  |
| `onMouseUp` | `React.MouseEventHandler<HTMLDivElement> | undefined` | — |  |
| `onMouseUpCapture` | `React.MouseEventHandler<HTMLDivElement> | undefined` | — |  |
| `onPointerCancel` | `React.PointerEventHandler<HTMLDivElement> | undefined` | — |  |
| `onPointerCancelCapture` | `React.PointerEventHandler<HTMLDivElement> | undefined` | — |  |
| `onPointerDown` | `React.PointerEventHandler<HTMLDivElement> | undefined` | — |  |
| `onPointerDownCapture` | `React.PointerEventHandler<HTMLDivElement> | undefined` | — |  |
| `onPointerEnter` | `React.PointerEventHandler<HTMLDivElement> | undefined` | — |  |
| `onPointerLeave` | `React.PointerEventHandler<HTMLDivElement> | undefined` | — |  |
| `onPointerMove` | `React.PointerEventHandler<HTMLDivElement> | undefined` | — |  |
| `onPointerMoveCapture` | `React.PointerEventHandler<HTMLDivElement> | undefined` | — |  |
| `onPointerOut` | `React.PointerEventHandler<HTMLDivElement> | undefined` | — |  |
| `onPointerOutCapture` | `React.PointerEventHandler<HTMLDivElement> | undefined` | — |  |
| `onPointerOver` | `React.PointerEventHandler<HTMLDivElement> | undefined` | — |  |
| `onPointerOverCapture` | `React.PointerEventHandler<HTMLDivElement> | undefined` | — |  |
| `onPointerUp` | `React.PointerEventHandler<HTMLDivElement> | undefined` | — |  |
| `onPointerUpCapture` | `React.PointerEventHandler<HTMLDivElement> | undefined` | — |  |
| `onScroll` | `React.UIEventHandler<HTMLDivElement> | undefined` | — |  |
| `onScrollCapture` | `React.UIEventHandler<HTMLDivElement> | undefined` | — |  |
| `onTouchCancel` | `React.TouchEventHandler<HTMLDivElement> | undefined` | — |  |
| `onTouchCancelCapture` | `React.TouchEventHandler<HTMLDivElement> | undefined` | — |  |
| `onTouchEnd` | `React.TouchEventHandler<HTMLDivElement> | undefined` | — |  |
| `onTouchEndCapture` | `React.TouchEventHandler<HTMLDivElement> | undefined` | — |  |
| `onTouchMove` | `React.TouchEventHandler<HTMLDivElement> | undefined` | — |  |
| `onTouchMoveCapture` | `React.TouchEventHandler<HTMLDivElement> | undefined` | — |  |
| `onTouchStart` | `React.TouchEventHandler<HTMLDivElement> | undefined` | — |  |
| `onTouchStartCapture` | `React.TouchEventHandler<HTMLDivElement> | undefined` | — |  |
| `onTransitionCancel` | `React.TransitionEventHandler<HTMLDivElement> | undefined` | — |  |
| `onTransitionCancelCapture` | `React.TransitionEventHandler<HTMLDivElement> | undefined` | — |  |
| `onTransitionEnd` | `React.TransitionEventHandler<HTMLDivElement> | undefined` | — |  |
| `onTransitionEndCapture` | `React.TransitionEventHandler<HTMLDivElement> | undefined` | — |  |
| `onTransitionRun` | `React.TransitionEventHandler<HTMLDivElement> | undefined` | — |  |
| `onTransitionRunCapture` | `React.TransitionEventHandler<HTMLDivElement> | undefined` | — |  |
| `onTransitionStart` | `React.TransitionEventHandler<HTMLDivElement> | undefined` | — |  |
| `onTransitionStartCapture` | `React.TransitionEventHandler<HTMLDivElement> | undefined` | — |  |
| `onWheel` | `React.WheelEventHandler<HTMLDivElement> | undefined` | — |  |
| `onWheelCapture` | `React.WheelEventHandler<HTMLDivElement> | undefined` | — |  |
| `render` | `DOMRenderFunction<"div", SheetRenderProps> | undefined` | — | Overrides the default DOM element with a custom render function. This allows rendering existing components with built-in styles and behaviors such as router links, animation libraries, and pre-styled components. Requirements: - You must render the expected element type (e.g. if `<button>` is expected, you cannot render an   `<a>`). - Only a single root DOM element can be rendered (no fragments). - You must pass through props and ref to the underlying DOM element, merging with your own prop   as appropriate. |
| `style` | `StyleOrFunction<SheetRenderProps> | undefined` | — | The inline [style](https://developer.mozilla.org/en-US/docs/Web/API/HTMLElement/style) for the element. A function may be provided to compute the style based on component state. |
| `suppressHydrationWarning` | `boolean | undefined` | — | A React-only flag that suppresses the warning React normally logs when an element's server-rendered content doesn't match the client render during hydration. See [React docs](https://react.dev/reference/react-dom/client/hydrateRoot#suppressing-unavoidable-hydration-mismatch-errors). |
| `swipeAnimation` | `string | undefined` | — | CSS `@keyframes` name for the animation that occurs while the user swipes. |
| `swipeAnimationRange` | `{ start?: number; end?: number; } | undefined` | — | The snap point indices between which the swipe animation occurs. If omitted, the animation will occur over the full range of the sheet's movement. |
| `translate` | `"no" | "yes" | undefined` | — |  |

### Sheet

| Name | Type | Default | Description |
|------|------|---------|-------------|
| `children` | `ChildrenOrFunction<SheetRenderProps>` | — | The children of the component. A function may be provided to alter the children based on component state. |
| `className` | `ClassNameOrFunction<SheetRenderProps> | undefined` | 'react-aria-Sheet' | The CSS [className](https://developer.mozilla.org/en-US/docs/Web/API/Element/className) for the element. A function may be provided to compute the class based on component state. |
| `dir` | `string | undefined` | — |  |
| `hidden` | `boolean | undefined` | — |  |
| `inert` | `boolean | undefined` | — |  |
| `lang` | `string | undefined` | — |  |
| `onAnimationEnd` | `React.AnimationEventHandler<HTMLDivElement> | undefined` | — |  |
| `onAnimationEndCapture` | `React.AnimationEventHandler<HTMLDivElement> | undefined` | — |  |
| `onAnimationIteration` | `React.AnimationEventHandler<HTMLDivElement> | undefined` | — |  |
| `onAnimationIterationCapture` | `React.AnimationEventHandler<HTMLDivElement> | undefined` | — |  |
| `onAnimationStart` | `React.AnimationEventHandler<HTMLDivElement> | undefined` | — |  |
| `onAnimationStartCapture` | `React.AnimationEventHandler<HTMLDivElement> | undefined` | — |  |
| `onAuxClick` | `React.MouseEventHandler<HTMLDivElement> | undefined` | — |  |
| `onAuxClickCapture` | `React.MouseEventHandler<HTMLDivElement> | undefined` | — |  |
| `onClick` | `React.MouseEventHandler<HTMLDivElement> | undefined` | — |  |
| `onClickCapture` | `React.MouseEventHandler<HTMLDivElement> | undefined` | — |  |
| `onContextMenu` | `React.MouseEventHandler<HTMLDivElement> | undefined` | — |  |
| `onContextMenuCapture` | `React.MouseEventHandler<HTMLDivElement> | undefined` | — |  |
| `onDoubleClick` | `React.MouseEventHandler<HTMLDivElement> | undefined` | — |  |
| `onDoubleClickCapture` | `React.MouseEventHandler<HTMLDivElement> | undefined` | — |  |
| `onGotPointerCapture` | `React.PointerEventHandler<HTMLDivElement> | undefined` | — |  |
| `onGotPointerCaptureCapture` | `React.PointerEventHandler<HTMLDivElement> | undefined` | — |  |
| `onLostPointerCapture` | `React.PointerEventHandler<HTMLDivElement> | undefined` | — |  |
| `onLostPointerCaptureCapture` | `React.PointerEventHandler<HTMLDivElement> | undefined` | — |  |
| `onMouseDown` | `React.MouseEventHandler<HTMLDivElement> | undefined` | — |  |
| `onMouseDownCapture` | `React.MouseEventHandler<HTMLDivElement> | undefined` | — |  |
| `onMouseEnter` | `React.MouseEventHandler<HTMLDivElement> | undefined` | — |  |
| `onMouseLeave` | `React.MouseEventHandler<HTMLDivElement> | undefined` | — |  |
| `onMouseMove` | `React.MouseEventHandler<HTMLDivElement> | undefined` | — |  |
| `onMouseMoveCapture` | `React.MouseEventHandler<HTMLDivElement> | undefined` | — |  |
| `onMouseOut` | `React.MouseEventHandler<HTMLDivElement> | undefined` | — |  |
| `onMouseOutCapture` | `React.MouseEventHandler<HTMLDivElement> | undefined` | — |  |
| `onMouseOver` | `React.MouseEventHandler<HTMLDivElement> | undefined` | — |  |
| `onMouseOverCapture` | `React.MouseEventHandler<HTMLDivElement> | undefined` | — |  |
| `onMouseUp` | `React.MouseEventHandler<HTMLDivElement> | undefined` | — |  |
| `onMouseUpCapture` | `React.MouseEventHandler<HTMLDivElement> | undefined` | — |  |
| `onPointerCancel` | `React.PointerEventHandler<HTMLDivElement> | undefined` | — |  |
| `onPointerCancelCapture` | `React.PointerEventHandler<HTMLDivElement> | undefined` | — |  |
| `onPointerDown` | `React.PointerEventHandler<HTMLDivElement> | undefined` | — |  |
| `onPointerDownCapture` | `React.PointerEventHandler<HTMLDivElement> | undefined` | — |  |
| `onPointerEnter` | `React.PointerEventHandler<HTMLDivElement> | undefined` | — |  |
| `onPointerLeave` | `React.PointerEventHandler<HTMLDivElement> | undefined` | — |  |
| `onPointerMove` | `React.PointerEventHandler<HTMLDivElement> | undefined` | — |  |
| `onPointerMoveCapture` | `React.PointerEventHandler<HTMLDivElement> | undefined` | — |  |
| `onPointerOut` | `React.PointerEventHandler<HTMLDivElement> | undefined` | — |  |
| `onPointerOutCapture` | `React.PointerEventHandler<HTMLDivElement> | undefined` | — |  |
| `onPointerOver` | `React.PointerEventHandler<HTMLDivElement> | undefined` | — |  |
| `onPointerOverCapture` | `React.PointerEventHandler<HTMLDivElement> | undefined` | — |  |
| `onPointerUp` | `React.PointerEventHandler<HTMLDivElement> | undefined` | — |  |
| `onPointerUpCapture` | `React.PointerEventHandler<HTMLDivElement> | undefined` | — |  |
| `onScroll` | `React.UIEventHandler<HTMLDivElement> | undefined` | — |  |
| `onScrollCapture` | `React.UIEventHandler<HTMLDivElement> | undefined` | — |  |
| `onTouchCancel` | `React.TouchEventHandler<HTMLDivElement> | undefined` | — |  |
| `onTouchCancelCapture` | `React.TouchEventHandler<HTMLDivElement> | undefined` | — |  |
| `onTouchEnd` | `React.TouchEventHandler<HTMLDivElement> | undefined` | — |  |
| `onTouchEndCapture` | `React.TouchEventHandler<HTMLDivElement> | undefined` | — |  |
| `onTouchMove` | `React.TouchEventHandler<HTMLDivElement> | undefined` | — |  |
| `onTouchMoveCapture` | `React.TouchEventHandler<HTMLDivElement> | undefined` | — |  |
| `onTouchStart` | `React.TouchEventHandler<HTMLDivElement> | undefined` | — |  |
| `onTouchStartCapture` | `React.TouchEventHandler<HTMLDivElement> | undefined` | — |  |
| `onTransitionCancel` | `React.TransitionEventHandler<HTMLDivElement> | undefined` | — |  |
| `onTransitionCancelCapture` | `React.TransitionEventHandler<HTMLDivElement> | undefined` | — |  |
| `onTransitionEnd` | `React.TransitionEventHandler<HTMLDivElement> | undefined` | — |  |
| `onTransitionEndCapture` | `React.TransitionEventHandler<HTMLDivElement> | undefined` | — |  |
| `onTransitionRun` | `React.TransitionEventHandler<HTMLDivElement> | undefined` | — |  |
| `onTransitionRunCapture` | `React.TransitionEventHandler<HTMLDivElement> | undefined` | — |  |
| `onTransitionStart` | `React.TransitionEventHandler<HTMLDivElement> | undefined` | — |  |
| `onTransitionStartCapture` | `React.TransitionEventHandler<HTMLDivElement> | undefined` | — |  |
| `onWheel` | `React.WheelEventHandler<HTMLDivElement> | undefined` | — |  |
| `onWheelCapture` | `React.WheelEventHandler<HTMLDivElement> | undefined` | — |  |
| `overscrollPadding` | `boolean | undefined` | — | Whether to add padding to the sheet so that it appears to continue outside the viewport. |
| `render` | `DOMRenderFunction<"div", SheetRenderProps> | undefined` | — | Overrides the default DOM element with a custom render function. This allows rendering existing components with built-in styles and behaviors such as router links, animation libraries, and pre-styled components. Requirements: - You must render the expected element type (e.g. if `<button>` is expected, you cannot render an   `<a>`). - Only a single root DOM element can be rendered (no fragments). - You must pass through props and ref to the underlying DOM element, merging with your own prop   as appropriate. |
| `slot` | `string | null | undefined` | — | A slot name for the component. Slots allow the component to receive props from a parent component. An explicit `null` value indicates that the local props completely override all props received from a parent. |
| `stackAnimation` | `string | undefined` | — | CSS `@keyframes` name for the animation that occurs when a sheet has descendants. |
| `style` | `StyleOrFunction<SheetRenderProps> | undefined` | — | The inline [style](https://developer.mozilla.org/en-US/docs/Web/API/HTMLElement/style) for the element. A function may be provided to compute the style based on component state. |
| `suppressHydrationWarning` | `boolean | undefined` | — | A React-only flag that suppresses the warning React normally logs when an element's server-rendered content doesn't match the client render during hydration. See [React docs](https://react.dev/reference/react-dom/client/hydrateRoot#suppressing-unavoidable-hydration-mismatch-errors). |
| `swipeAnimation` | `string | undefined` | — | CSS `@keyframes` name for the animation that occurs while the user swipes. |
| `swipeAnimationRange` | `{ start?: number; end?: number; } | undefined` | — | The snap point indices between which the swipe animation occurs. If omitted, the animation will occur over the full range of the sheet's movement. |
| `translate` | `"no" | "yes" | undefined` | — |  |

### SheetContent

| Name | Type | Default | Description |
|------|------|---------|-------------|
| `aria-describedby` | `string | undefined` | — | Identifies the element (or elements) that describes the object. |
| `aria-details` | `string | undefined` | — | Identifies the element (or elements) that provide a detailed, extended description for the object. |
| `aria-label` | `string | undefined` | — | Defines a string value that labels the current element. |
| `aria-labelledby` | `string | undefined` | — | Identifies the element (or elements) that labels the current element. |
| `children` | `((opts: DialogRenderProps) => React.ReactNode) | React.ReactNode` | — | Children of the dialog. A function may be provided to access a function to close the dialog. |
| `className` | `string | undefined` | 'react-aria-SheetContent' | The CSS [className](https://developer.mozilla.org/en-US/docs/Web/API/Element/className) for the element. |
| `dir` | `string | undefined` | — |  |
| `hidden` | `boolean | undefined` | — |  |
| `id` | `string | undefined` | — | The element's unique identifier. See [MDN](https://developer.mozilla.org/en-US/docs/Web/HTML/Global_attributes/id). |
| `inert` | `boolean | undefined` | — |  |
| `lang` | `string | undefined` | — |  |
| `onAnimationEnd` | `React.AnimationEventHandler<HTMLElement> | undefined` | — |  |
| `onAnimationEndCapture` | `React.AnimationEventHandler<HTMLElement> | undefined` | — |  |
| `onAnimationIteration` | `React.AnimationEventHandler<HTMLElement> | undefined` | — |  |
| `onAnimationIterationCapture` | `React.AnimationEventHandler<HTMLElement> | undefined` | — |  |
| `onAnimationStart` | `React.AnimationEventHandler<HTMLElement> | undefined` | — |  |
| `onAnimationStartCapture` | `React.AnimationEventHandler<HTMLElement> | undefined` | — |  |
| `onAuxClick` | `React.MouseEventHandler<HTMLElement> | undefined` | — |  |
| `onAuxClickCapture` | `React.MouseEventHandler<HTMLElement> | undefined` | — |  |
| `onClick` | `React.MouseEventHandler<HTMLElement> | undefined` | — |  |
| `onClickCapture` | `React.MouseEventHandler<HTMLElement> | undefined` | — |  |
| `onContextMenu` | `React.MouseEventHandler<HTMLElement> | undefined` | — |  |
| `onContextMenuCapture` | `React.MouseEventHandler<HTMLElement> | undefined` | — |  |
| `onDoubleClick` | `React.MouseEventHandler<HTMLElement> | undefined` | — |  |
| `onDoubleClickCapture` | `React.MouseEventHandler<HTMLElement> | undefined` | — |  |
| `onGotPointerCapture` | `React.PointerEventHandler<HTMLElement> | undefined` | — |  |
| `onGotPointerCaptureCapture` | `React.PointerEventHandler<HTMLElement> | undefined` | — |  |
| `onLostPointerCapture` | `React.PointerEventHandler<HTMLElement> | undefined` | — |  |
| `onLostPointerCaptureCapture` | `React.PointerEventHandler<HTMLElement> | undefined` | — |  |
| `onMouseDown` | `React.MouseEventHandler<HTMLElement> | undefined` | — |  |
| `onMouseDownCapture` | `React.MouseEventHandler<HTMLElement> | undefined` | — |  |
| `onMouseEnter` | `React.MouseEventHandler<HTMLElement> | undefined` | — |  |
| `onMouseLeave` | `React.MouseEventHandler<HTMLElement> | undefined` | — |  |
| `onMouseMove` | `React.MouseEventHandler<HTMLElement> | undefined` | — |  |
| `onMouseMoveCapture` | `React.MouseEventHandler<HTMLElement> | undefined` | — |  |
| `onMouseOut` | `React.MouseEventHandler<HTMLElement> | undefined` | — |  |
| `onMouseOutCapture` | `React.MouseEventHandler<HTMLElement> | undefined` | — |  |
| `onMouseOver` | `React.MouseEventHandler<HTMLElement> | undefined` | — |  |
| `onMouseOverCapture` | `React.MouseEventHandler<HTMLElement> | undefined` | — |  |
| `onMouseUp` | `React.MouseEventHandler<HTMLElement> | undefined` | — |  |
| `onMouseUpCapture` | `React.MouseEventHandler<HTMLElement> | undefined` | — |  |
| `onPointerCancel` | `React.PointerEventHandler<HTMLElement> | undefined` | — |  |
| `onPointerCancelCapture` | `React.PointerEventHandler<HTMLElement> | undefined` | — |  |
| `onPointerDown` | `React.PointerEventHandler<HTMLElement> | undefined` | — |  |
| `onPointerDownCapture` | `React.PointerEventHandler<HTMLElement> | undefined` | — |  |
| `onPointerEnter` | `React.PointerEventHandler<HTMLElement> | undefined` | — |  |
| `onPointerLeave` | `React.PointerEventHandler<HTMLElement> | undefined` | — |  |
| `onPointerMove` | `React.PointerEventHandler<HTMLElement> | undefined` | — |  |
| `onPointerMoveCapture` | `React.PointerEventHandler<HTMLElement> | undefined` | — |  |
| `onPointerOut` | `React.PointerEventHandler<HTMLElement> | undefined` | — |  |
| `onPointerOutCapture` | `React.PointerEventHandler<HTMLElement> | undefined` | — |  |
| `onPointerOver` | `React.PointerEventHandler<HTMLElement> | undefined` | — |  |
| `onPointerOverCapture` | `React.PointerEventHandler<HTMLElement> | undefined` | — |  |
| `onPointerUp` | `React.PointerEventHandler<HTMLElement> | undefined` | — |  |
| `onPointerUpCapture` | `React.PointerEventHandler<HTMLElement> | undefined` | — |  |
| `onScroll` | `React.UIEventHandler<HTMLElement> | undefined` | — |  |
| `onScrollCapture` | `React.UIEventHandler<HTMLElement> | undefined` | — |  |
| `onTouchCancel` | `React.TouchEventHandler<HTMLElement> | undefined` | — |  |
| `onTouchCancelCapture` | `React.TouchEventHandler<HTMLElement> | undefined` | — |  |
| `onTouchEnd` | `React.TouchEventHandler<HTMLElement> | undefined` | — |  |
| `onTouchEndCapture` | `React.TouchEventHandler<HTMLElement> | undefined` | — |  |
| `onTouchMove` | `React.TouchEventHandler<HTMLElement> | undefined` | — |  |
| `onTouchMoveCapture` | `React.TouchEventHandler<HTMLElement> | undefined` | — |  |
| `onTouchStart` | `React.TouchEventHandler<HTMLElement> | undefined` | — |  |
| `onTouchStartCapture` | `React.TouchEventHandler<HTMLElement> | undefined` | — |  |
| `onTransitionCancel` | `React.TransitionEventHandler<HTMLElement> | undefined` | — |  |
| `onTransitionCancelCapture` | `React.TransitionEventHandler<HTMLElement> | undefined` | — |  |
| `onTransitionEnd` | `React.TransitionEventHandler<HTMLElement> | undefined` | — |  |
| `onTransitionEndCapture` | `React.TransitionEventHandler<HTMLElement> | undefined` | — |  |
| `onTransitionRun` | `React.TransitionEventHandler<HTMLElement> | undefined` | — |  |
| `onTransitionRunCapture` | `React.TransitionEventHandler<HTMLElement> | undefined` | — |  |
| `onTransitionStart` | `React.TransitionEventHandler<HTMLElement> | undefined` | — |  |
| `onTransitionStartCapture` | `React.TransitionEventHandler<HTMLElement> | undefined` | — |  |
| `onWheel` | `React.WheelEventHandler<HTMLElement> | undefined` | — |  |
| `onWheelCapture` | `React.WheelEventHandler<HTMLElement> | undefined` | — |  |
| `render` | `DOMRenderFunction<"section", undefined> | undefined` | — | Overrides the default DOM element with a custom render function. This allows rendering existing components with built-in styles and behaviors such as router links, animation libraries, and pre-styled components. Requirements: - You must render the expected element type (e.g. if `<button>` is expected, you cannot render an   `<a>`). - Only a single root DOM element can be rendered (no fragments). - You must pass through props and ref to the underlying DOM element, merging with your own prop   as appropriate. |
| `role` | `"alertdialog" | "dialog" | undefined` | 'dialog' | The accessibility role for the dialog. |
| `slot` | `string | null | undefined` | — | A slot name for the component. Slots allow the component to receive props from a parent component. An explicit `null` value indicates that the local props completely override all props received from a parent. |
| `style` | `React.CSSProperties | undefined` | — | The inline [style](https://developer.mozilla.org/en-US/docs/Web/API/HTMLElement/style) for the element. |
| `suppressHydrationWarning` | `boolean | undefined` | — | A React-only flag that suppresses the warning React normally logs when an element's server-rendered content doesn't match the client render during hydration. See [React docs](https://react.dev/reference/react-dom/client/hydrateRoot#suppressing-unavoidable-hydration-mismatch-errors). |
| `translate` | `"no" | "yes" | undefined` | — |  |
