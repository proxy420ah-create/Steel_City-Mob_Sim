# Test the GetBool logic equivalent
def get_bool(d, key, fallback):
    if d and key in d:
        return bool(d[key])
    return fallback

# Test cases
test_cases = [
    {"use_position_for_anchor": True},
    {"use_position_for_anchor": False},
    {"use_position_for_anchor": "true"},
    {"use_position_for_anchor": "false"},
    {"use_position_for_anchor": 1},
    {"use_position_for_anchor": 0},
    {},  # missing key
]

for i, case in enumerate(test_cases):
    result = get_bool(case, "use_position_for_anchor", False)
    print(f"Case {i}: {case} -> {result} (type: {type(result)})")
