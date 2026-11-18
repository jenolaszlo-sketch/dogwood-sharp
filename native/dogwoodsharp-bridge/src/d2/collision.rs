use cedar_policy_core::{
    ast::{InternalName, Name},
    extensions::Extensions,
    validator::{
        RawName,
        json_schema::{CommonTypeId, Fragment, Type, TypeVariant},
    },
};
use dogwood_language::LoweredPolicySet;
use serde::Serialize;

#[derive(Serialize)]
#[serde(rename_all = "camelCase")]
pub(super) struct Collision {
    pub(super) field: String,
    pub(super) action_namespace: Option<String>,
    pub(super) action_id: String,
}

pub(super) fn find(source: &str, lowered: &LoweredPolicySet) -> Result<Option<Collision>, String> {
    let mut fields = lowered.nonrelativized_temporal_fields().peekable();
    if fields.peek().is_none() {
        return Ok(None);
    }

    // Match the BOM handling of PolicySchema. A successful Dogwood lowering
    // has already passed its action-schema nesting check.
    let source = source.strip_prefix('\u{feff}').unwrap_or(source);
    let (original, _) =
        Fragment::<RawName>::from_cedarschema_str(source, Extensions::all_available())
            .map_err(|error| error.to_string())?;
    let mut qualified = None;
    for field in fields {
        // Dogwood uses this same scope resolution when inserting each field.
        // The actions are sorted and deduplicated by the pinned implementation.
        for action in &field.target_actions {
            let namespace = namespace_key(action.namespace.as_deref())?;
            let original_context = context(&original, &namespace, &action.id)?;
            let has_field = if let Type::Type {
                ty: TypeVariant::Record(record),
                ..
            } = original_context
            {
                // Inspect the declared key even if augmentation would mask an
                // invalid type on this attribute in the original schema.
                record.attributes.contains_key(field.id.as_str())
            } else {
                if qualified.is_none() {
                    let fragment = original
                        .to_internal_name_fragment_with_resolved_types()
                        .map_err(|error| error.to_string())?;
                    let count = fragment.0.values().map(|ns| ns.common_types.len()).sum();
                    qualified = Some((fragment, count));
                }
                let (fragment, count) = qualified
                    .as_ref()
                    .expect("qualified schema was initialized");
                resolved_has_field(fragment, &namespace, &action.id, &field.id, *count)?
            };
            if has_field {
                return Ok(Some(Collision {
                    field: field.id.clone(),
                    action_namespace: action.namespace.clone(),
                    action_id: action.id.clone(),
                }));
            }
        }
    }
    Ok(None)
}

fn namespace_key(namespace: Option<&str>) -> Result<Option<Name>, String> {
    namespace
        .map(|name| name.parse::<Name>().map_err(|error| error.to_string()))
        .transpose()
}

fn context<'a, N>(
    fragment: &'a Fragment<N>,
    namespace: &Option<Name>,
    id: &str,
) -> Result<&'a Type<N>, String> {
    fragment
        .0
        .get(namespace)
        .and_then(|ns| ns.actions.get(id))
        .and_then(|action| action.applies_to.as_ref())
        .map(|applies_to| &applies_to.context.0)
        .ok_or_else(|| format!("Original context is missing for action {id:?}."))
}

fn resolved_has_field(
    fragment: &Fragment<InternalName>,
    namespace: &Option<Name>,
    action: &str,
    field: &str,
    common_type_count: usize,
) -> Result<bool, String> {
    let mut current = context(fragment, namespace, action)?;
    // Cedar has qualified all names. Only follow exact common-type references
    // until the outer record is found. Nested attributes are outside the
    // collision scope and their types are immaterial.
    for _ in 0..=common_type_count {
        match current {
            Type::Type {
                ty: TypeVariant::Record(record),
                ..
            } => return Ok(record.attributes.contains_key(field)),
            Type::CommonTypeRef { type_name, .. } => {
                let namespace = type_name.namespace();
                let namespace = namespace_key((!namespace.is_empty()).then_some(&namespace))?;
                let basename = type_name
                    .basename()
                    .clone()
                    .try_into()
                    .map_err(|_| format!("Invalid common type {type_name}."))?;
                let key = CommonTypeId::new(basename).map_err(|error| error.to_string())?;
                current = &fragment
                    .0
                    .get(&namespace)
                    .and_then(|ns| ns.common_types.get(&key))
                    .ok_or_else(|| format!("Resolved common type {type_name} is missing."))?
                    .ty;
            }
            _ => {
                return Err(format!(
                    "Original context for action {action:?} is not a record."
                ));
            }
        }
    }
    Err(format!(
        "Original context for action {action:?} contains a type cycle."
    ))
}
