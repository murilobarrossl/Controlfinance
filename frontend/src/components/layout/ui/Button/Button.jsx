import "./Button.css"

export default function Button({
  as: Component = "button",
  variant = "primary",
  size = "md",
  children,
  className = "",
  ...props
}) {
  const classes = ["btn", `btn--${variant}`, `btn--${size}`, className]
    .filter(Boolean)
    .join(" ");

  return (
    <Component className={classes} {...props}>
      {children}
    </Component>
  );
}
