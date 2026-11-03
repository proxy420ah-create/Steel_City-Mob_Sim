#!/usr/bin/env python3
"""Bracket-balance scan for the dashboard's <script> block (no node on this box).
Skips strings, comments, regex-ish division contexts, and template literals."""
import re, sys

p = sys.argv[1] if len(sys.argv) > 1 else __file__.replace('_deckcheck_scan.py', 'DELIBERATION_MACHINE.html')
s = open(p, encoding='utf-8').read()
m = re.search(r'<script>(.*)</script>', s, re.S)
code = m.group(1)

stack = []
i, n = 0, len(code)
line = 1
state = 'code'          # code | squote | dquote | tmpl | line_comment | block_comment | regex
rx_class = False        # inside a [...] char class while state == 'regex'
prev_sig = ''           # last significant char for regex detection
pairs = {')':'(', ']':'[', '}':'{'}
opens = set('([{')
errors = []
while i < n:
    c = code[i]
    nxt = code[i+1] if i+1 < n else ''
    if c == '\n':
        line += 1
    if state == 'line_comment':
        if c == '\n': state = 'code'
    elif state == 'block_comment':
        if c == '*' and nxt == '/': state = 'code'; i += 1
    elif state == 'squote':
        if c == '\\': i += 1
        elif c == "'": state = 'code'
    elif state == 'dquote':
        if c == '\\': i += 1
        elif c == '"': state = 'code'
    elif state == 'tmpl':
        if c == '\\': i += 1
        elif c == '`': state = 'code'
        elif c == '$' and nxt == '{': stack.append(('${', line)); state = 'code'; i += 1
    elif state == 'regex':
        if c == '\\': i += 1
        elif c == '[': rx_class = True
        elif c == ']': rx_class = False
        elif c == '/' and not rx_class: state = 'code'
    elif state == 'code':
        if c == '/' and nxt == '/': state = 'line_comment'; i += 1
        elif c == '/' and nxt == '*': state = 'block_comment'; i += 1
        elif c == '/' and (prev_sig == '' or prev_sig in '(,=:[!&|?{};+-*%<>~^'): state = 'regex'; rx_class = False
        elif c == "'": state = 'squote'
        elif c == '"': state = 'dquote'
        elif c == '`': state = 'tmpl'
        elif c in opens: stack.append((c, line))
        elif c in pairs:
            if not stack:
                errors.append((line, 'unmatched ' + c)); break
            top, tl = stack.pop()
            if top == '${':      # closing a template interpolation returns to tmpl
                state = 'tmpl'
            elif top != pairs[c]:
                errors.append((line, 'expected close of %r (opened line %d), got %r' % (top, tl, c))); break
    if state == 'code' and not c.isspace():
        prev_sig = c
    i += 1

if stack and not errors:
    errors.append((stack[-1][1], 'unclosed %r (depth %d)' % (stack[-1][0], len(stack))))
if errors:
    print('MISMATCH at script-line', errors[0][0], '-', errors[0][1])
    sys.exit(1)
print('balanced OK —', n, 'chars of script')
